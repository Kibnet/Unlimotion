using System.Text;
using Newtonsoft.Json;

namespace Unlimotion.Storage;

public static class JsonRepairingReader
{
    public static T DeserializeWithRepair<T>(string fullPath, JsonSerializer jsonSerializer, bool saveRepairedSidecar = false)
    {
        ArgumentNullException.ThrowIfNull(jsonSerializer);

        try
        {
            using var reader = File.OpenText(fullPath);
            using var jsonReader = new JsonTextReader(reader);
            return jsonSerializer.Deserialize<T>(jsonReader)!;
        }
        catch (JsonReaderException)
        {
            // Fall through to the small historical repair pass below.
        }

        var original = File.ReadAllText(fullPath, Encoding.UTF8);
        var repaired = FixMissingCommas(original);

        try
        {
            using var sr = new StringReader(repaired);
            using var jsonReader = new JsonTextReader(sr);
            var result = jsonSerializer.Deserialize<T>(jsonReader);

            if (saveRepairedSidecar)
            {
                File.WriteAllText(fullPath + ".repaired.json", repaired, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            return result!;
        }
        catch (JsonReaderException ex)
        {
            throw new JsonReaderException(
                $"Failed to repair JSON file '{fullPath}'. Repaired preview: {Preview(repaired, 200)}",
                ex);
        }
    }

    private static string Preview(string value, int length) =>
        value.Length <= length ? value : value[..length] + "...";

    private enum ContextKind
    {
        Object,
        Array
    }

    private enum ObjectState
    {
        ExpectKey,
        ExpectColon,
        ExpectValue,
        ExpectCommaOrEnd
    }

    private struct Context
    {
        public ContextKind Kind;
        public ObjectState ObjectState;

        public Context(ContextKind kind)
        {
            Kind = kind;
            ObjectState = ObjectState.ExpectKey;
        }
    }

    public static string FixMissingCommas(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var sb = new StringBuilder(input.Length + Math.Min(1024, input.Length / 8));
        var stack = new Stack<Context>();
        var inString = false;
        var escape = false;

        int NextNonWhitespaceIndex(int index)
        {
            for (var i = index + 1; i < input.Length; i++)
            {
                if (!char.IsWhiteSpace(input[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        char NextNonWhitespaceChar(int index)
        {
            var next = NextNonWhitespaceIndex(index);
            return next >= 0 ? input[next] : '\0';
        }

        static bool IsValueStart(char value) =>
            value == '"' || value == '{' || value == '[' || value == '-' || value == '+' || char.IsDigit(value) ||
            value is 't' or 'f' or 'n';

        static bool IsCleanValueEndChar(char value) =>
            value is '"' or '}' or ']' or 'e' or 'E' or 'l' or 'L';

        void AfterValueEndedAt(int index)
        {
            if (stack.Count == 0)
            {
                return;
            }

            var parent = stack.Peek();
            var next = NextNonWhitespaceChar(index);

            if (parent.Kind == ContextKind.Array)
            {
                if (IsValueStart(next))
                {
                    sb.Append(',');
                }

                return;
            }

            if (next == '"')
            {
                sb.Append(',');
                return;
            }

            var top = stack.Pop();
            if (top.ObjectState == ObjectState.ExpectValue)
            {
                top.ObjectState = ObjectState.ExpectCommaOrEnd;
            }

            stack.Push(top);
        }

        for (var i = 0; i < input.Length; i++)
        {
            var current = input[i];
            sb.Append(current);

            if (inString)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (current == '\\')
                {
                    escape = true;
                    continue;
                }

                if (current == '"')
                {
                    inString = false;

                    if (stack.Count > 0)
                    {
                        var top = stack.Peek();
                        if (top.Kind == ContextKind.Object)
                        {
                            var updated = stack.Pop();
                            if (updated.ObjectState == ObjectState.ExpectKey)
                            {
                                updated.ObjectState = ObjectState.ExpectColon;
                            }
                            else if (updated.ObjectState == ObjectState.ExpectValue)
                            {
                                updated.ObjectState = ObjectState.ExpectCommaOrEnd;
                                if (NextNonWhitespaceChar(i) == '"')
                                {
                                    sb.Append(',');
                                    updated.ObjectState = ObjectState.ExpectKey;
                                    stack.Push(updated);
                                    continue;
                                }
                            }

                            stack.Push(updated);
                        }
                        else if (IsValueStart(NextNonWhitespaceChar(i)))
                        {
                            sb.Append(',');
                        }
                    }
                }

                continue;
            }

            if (char.IsWhiteSpace(current))
            {
                continue;
            }

            if (current == '"')
            {
                if (stack.Count > 0 && stack.Peek().Kind == ContextKind.Object)
                {
                    var top = stack.Peek();
                    if (top.ObjectState == ObjectState.ExpectCommaOrEnd)
                    {
                        sb.Length--;
                        sb.Append(',').Append('"');
                    }
                }

                inString = true;
                continue;
            }

            if (current == '{')
            {
                stack.Push(new Context(ContextKind.Object));
                continue;
            }

            if (current == '[')
            {
                stack.Push(new Context(ContextKind.Array));
                continue;
            }

            if (current == '}')
            {
                if (stack.Count > 0 && stack.Peek().Kind == ContextKind.Object)
                {
                    stack.Pop();
                }

                AfterValueEndedAt(i);
                continue;
            }

            if (current == ']')
            {
                if (stack.Count > 0 && stack.Peek().Kind == ContextKind.Array)
                {
                    stack.Pop();
                }

                AfterValueEndedAt(i);
                continue;
            }

            if (current == ':')
            {
                if (stack.Count > 0 && stack.Peek().Kind == ContextKind.Object)
                {
                    var top = stack.Pop();
                    top.ObjectState = ObjectState.ExpectValue;
                    stack.Push(top);
                }

                continue;
            }

            if (current == ',')
            {
                if (stack.Count > 0 && stack.Peek().Kind == ContextKind.Object)
                {
                    var top = stack.Pop();
                    top.ObjectState = ObjectState.ExpectKey;
                    stack.Push(top);
                }

                continue;
            }

            if (IsCleanValueEndChar(current))
            {
                AfterValueEndedAt(i);
            }
        }

        return sb.ToString();
    }
}
