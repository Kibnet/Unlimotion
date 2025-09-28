using System.Collections.Generic;

namespace Unlimotion;

using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

public static class JsonRepairingReader
{
    /// <summary>
    /// Безопасная загрузка из TextReader: сначала обычный парсинг, при ошибке — авто-ремонт и повтор.
    /// </summary>
    public static T DeserializeWithRepair<T>(TextReader reader, JsonSerializer jsonSerializer, Action<string>? saveRepaired = null)
    {
        if (jsonSerializer == null) throw new ArgumentNullException(nameof(jsonSerializer));
        if (reader == null) throw new ArgumentNullException(nameof(reader));

        // Попытка 1: прямой потоковый парсинг
        try
        {
            using var jsonReader = new JsonTextReader(reader);
            return jsonSerializer.Deserialize<T>(jsonReader);
        }
        catch (JsonReaderException)
        {
            // пойдём в ремонт — но reader уже прочитан;
            // поэтому вызывающий должен передать текст повторно (см. перегрузку для Stream ниже).
            throw;
        }
    }

    /// <summary>
    /// Безопасная загрузка из Stream. Поток будет целиком прочитан в память.
    /// Сначала обычный парсинг, при ошибке — авто-ремонт запятых и повторная попытка.
    /// </summary>
    public static T DeserializeWithRepair<T>(Stream input, JsonSerializer jsonSerializer, bool saveRepairedSidecar = false, string? sidecarPath = null)
    {
        if (jsonSerializer == null) throw new ArgumentNullException(nameof(jsonSerializer));
        if (input == null) throw new ArgumentNullException(nameof(input));

        // Скопируем в память, чтобы можно было делать несколько попыток
        using var ms = new MemoryStream();
        input.CopyTo(ms);

        // Попытка 1: обычный разбор
        try
        {
            ms.Position = 0;
            using var sr1 = new StreamReader(ms, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            using var jr1 = new JsonTextReader(sr1);
            var ok = jsonSerializer.Deserialize<T>(jr1);
            return ok!;
        }
        catch (JsonReaderException)
        {
            // Попытка 2: ремонт и повтор
            ms.Position = 0;
            using var sr2 = new StreamReader(ms, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var original = sr2.ReadToEnd();
            var repaired = FixMissingCommas(original);

            try
            {
                using var sr3 = new StringReader(repaired);
                using var jr3 = new JsonTextReader(sr3);
                var result = jsonSerializer.Deserialize<T>(jr3);

                if (saveRepairedSidecar && !string.IsNullOrEmpty(sidecarPath))
                {
                    // На Android SAF обычно нет прямого пути — просто не сохраняем sidecar.
                    try
                    {
                        File.WriteAllText(sidecarPath!, repaired, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    }
                    catch { /* ignore sidecar errors */ }
                }

                return result!;
            }
            catch (JsonReaderException ex2)
            {
                throw new JsonReaderException(
                    $"Не удалось распарсить JSON даже после авто-ремонта запятых. " +
                    $"Первые 200 символов после ремонта: {Preview(repaired, 200)}", ex2);
            }
        }
    }

    /// <summary>
    /// Безопасная загрузка JSON-файла: сначала обычный парсинг, при ошибке — авто-ремонт запятых и повторная попытка.
    /// </summary>
    public static T DeserializeWithRepair<T>(string fullPath, JsonSerializer jsonSerializer, bool saveRepairedSidecar = false)
    {
        if (jsonSerializer == null) throw new ArgumentNullException(nameof(jsonSerializer));

        // 1) Попытка штатного потокового разбора
        try
        {
            using var reader = File.OpenText(fullPath);
            using var jsonReader = new JsonTextReader(reader);
            return jsonSerializer.Deserialize<T>(jsonReader);
        }
        catch (JsonReaderException)
        {
            // упадём на ремонт
        }

        // 2) Ремонт и повторная попытка
        string original = File.ReadAllText(fullPath, Encoding.UTF8);
        string repaired = FixMissingCommas(original);

        try
        {
            using var sr = new StringReader(repaired);
            using var jsonReader2 = new JsonTextReader(sr);
            var result = jsonSerializer.Deserialize<T>(jsonReader2);

            if (saveRepairedSidecar)
            {
                var sidecar = fullPath + ".repaired.json";
                File.WriteAllText(sidecar, repaired, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            return result!;
        }
        catch (JsonReaderException ex2)
        {
            // Чтобы было понятно, что и как пытались чинить
            throw new JsonReaderException(
                $"Не удалось распарсить JSON даже после авто-ремонта запятых. Файл: {fullPath}. " +
                $"Первые 200 символов после ремонта: {Preview(repaired, 200)}",
                ex2);
        }
    }

    private static string Preview(string s, int len) =>
        s.Length <= len ? s : s.Substring(0, len) + "…";

    // --- Ремонт запятых (адаптировано под ваш кейс) ---
    enum CtxKind { Obj, Arr }
    enum ObjState { ExpectKey, ExpectColon, ExpectValue, ExpectCommaOrEnd }
    private struct Ctx { public CtxKind Kind; public ObjState ObjSt; public Ctx(CtxKind k) { Kind = k; ObjSt = ObjState.ExpectKey; } }

    /// <summary>Чинит пропущенные запятые между элементами массивов и свойствами объектов. Учитывает строки и экранирование.</summary>
    public static string FixMissingCommas(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var sb = new StringBuilder(input.Length + Math.Min(1024, input.Length / 8));
        var stack = new Stack<Ctx>();
        bool inString = false, escape = false;

        int NextNonWsIndex(int i)
        {
            for (int j = i + 1; j < input.Length; j++)
                if (!char.IsWhiteSpace(input[j])) return j;
            return -1;
        }

        char NextNonWsChar(int i)
        {
            var j = NextNonWsIndex(i);
            return j >= 0 ? input[j] : '\0';
        }

        static bool IsValueStart(char c) =>
            c == '"' || c == '{' || c == '[' || c == '-' || c == '+' || char.IsDigit(c) ||
            c == 't' || c == 'f' || c == 'n'; // true/false/null

        // Не включаем цифры сюда, чтобы не ломать числа; строки/объекты/массивы/литералы ok.
        static bool IsCleanValueEndChar(char c) =>
            c == '"' || c == '}' || c == ']' || c == 'e' || c == 'E' || c == 'l' || c == 'L';

        void AfterValueEndedAt(int i /*позиция завершающего символа*/)
        {
            if (stack.Count == 0) return;
            var parent = stack.Peek();
            var nn = NextNonWsChar(i);

            if (parent.Kind == CtxKind.Arr)
            {
                // В массиве: если далее старт нового значения — нужна запятая.
                if (IsValueStart(nn)) sb.Append(',');
            }
            else // Obj
            {
                // В объекте: после значения, если далее начинается новый ключ (кавычка) — вставить запятую.
                if (nn == '"')
                {
                    sb.Append(',');
                }
                else
                {
                    var top = stack.Pop();
                    if (top.ObjSt == ObjState.ExpectValue)
                        top.ObjSt = ObjState.ExpectCommaOrEnd;
                    stack.Push(top);
                }
            }
        }

        for (int i = 0; i < input.Length; i++)
        {
            char ch = input[i];
            sb.Append(ch);

            if (inString)
            {
                if (escape) { escape = false; continue; }
                if (ch == '\\') { escape = true; continue; }

                if (ch == '"')
                {
                    // Строка закончилась ИМЕННО сейчас — здесь решаем вопрос с запятой.
                    inString = false;

                    if (stack.Count > 0)
                    {
                        var top = stack.Peek();
                        if (top.Kind == CtxKind.Obj)
                        {
                            // Если это был ключ — дальше ждём двоеточие; если значение — возможна запятая.
                            var t = stack.Pop();
                            if (t.ObjSt == ObjState.ExpectKey)
                            {
                                t.ObjSt = ObjState.ExpectColon;
                            }
                            else if (t.ObjSt == ObjState.ExpectValue)
                            {
                                t.ObjSt = ObjState.ExpectCommaOrEnd;
                                var nn = NextNonWsChar(i);
                                if (nn == '"') {
                                    sb.Append(','); // новый ключ без запятой
                                    continue;
                                }
                            }
                            stack.Push(t);
                        }
                        else // массив: строковое значение закончилось — возможно, дальше ещё одно значение
                        {
                            var nn = NextNonWsChar(i);
                            if (IsValueStart(nn))
                            {
                                sb.Append(',');
                            }
                        }
                    }
                }
                continue; // обработали закрывающую кавычку внутри строки
            }

            if (char.IsWhiteSpace(ch)) continue;

            if (ch == '"')
            {
                // Начало строки.
                if (stack.Count > 0 && stack.Peek().Kind == CtxKind.Obj)
                {
                    // Если объект уже ждёт запятую/конец, а пришёл новый ключ — вставим запятую ПЕРЕД этой кавычкой.
                    var top = stack.Peek();
                    if (top.ObjSt == ObjState.ExpectCommaOrEnd)
                    {
                        sb.Length--;        // убрать только что добавленную кавычку
                        sb.Append(',').Append('"');
                    }
                }
                inString = true;
                continue;
            }

            if (ch == '{') { stack.Push(new Ctx(CtxKind.Obj)); continue; }
            if (ch == '[') { stack.Push(new Ctx(CtxKind.Arr)); continue; }

            if (ch == '}')
            {
                // Закрылся объект — это «одно значение» для родителя
                if (stack.Count > 0 && stack.Peek().Kind == CtxKind.Obj) stack.Pop();
                AfterValueEndedAt(i);
                continue;
            }

            if (ch == ']')
            {
                // Закрылся массив — это «одно значение» для родителя
                if (stack.Count > 0 && stack.Peek().Kind == CtxKind.Arr) stack.Pop();
                AfterValueEndedAt(i);
                continue;
            }

            if (ch == ':')
            {
                if (stack.Count > 0 && stack.Peek().Kind == CtxKind.Obj)
                {
                    var t = stack.Pop();
                    t.ObjSt = ObjState.ExpectValue;
                    stack.Push(t);
                }
                continue;
            }

            if (ch == ',')
            {
                if (stack.Count > 0 && stack.Peek().Kind == CtxKind.Obj)
                {
                    var t = stack.Pop();
                    t.ObjSt = ObjState.ExpectKey;
                    stack.Push(t);
                }
                continue;
            }

            // Литералы true/false/null: срабатываем на их последних буквах (e / l).
            if (IsCleanValueEndChar(ch))
            {
                // Завершилось "нестроковое" значение (литерал/влож.структура) — возможно, нужна запятая.
                AfterValueEndedAt(i);
            }
        }

        return sb.ToString();
    }
}