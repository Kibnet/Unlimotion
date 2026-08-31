using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ReactiveUI.Avalonia;
using ReactiveUI.Builder;
using TUnit.Core;
using Unlimotion.ViewModel;

namespace Unlimotion.Test;

public static class ReactiveUiSessionHooks
{
    private static readonly ConcurrentDictionary<string, string[]> ResourceScopes = new();

    [BeforeEvery(HookType.Test)]
    public static void RecordTestStart(TestContext context)
    {
        var type = context.Metadata.TestDetails.Class.ClassType;
        var resources = GetDeclaredResources(type, context.Metadata.TestName);
        ResourceScopes[context.Id] = resources;
        TestExecutionTrace.Write("test-lifecycle", context.Metadata.DisplayName, "started",
            details: new { className = type.FullName, method = context.Metadata.TestName, declaredResources = resources });
        foreach (var resource in resources) TestExecutionTrace.Write("scheduler-scope", resource, "entered");
    }

    [AfterEvery(HookType.Test)]
    public static void RecordTestEnd(TestContext context)
    {
        if (ResourceScopes.TryRemove(context.Id, out var resources))
            foreach (var resource in resources) TestExecutionTrace.Write("scheduler-scope", resource, "left");
        TestExecutionTrace.Write("test-lifecycle", context.Metadata.DisplayName, "finished");
        TestExecutionTrace.ThrowPendingErrors(context.Id);
    }

    [After(TestSession)]
    public static void ReportSessionTraceErrors() => TestExecutionTrace.ThrowPendingErrors();

    private static string[] GetDeclaredResources(Type type, string methodName)
    {
        var classAttributes = type.GetCustomAttributesData();
        var methodAttributes = type.GetMethods().Where(method => method.Name == methodName)
            .SelectMany(method => method.GetCustomAttributesData()).ToArray();
        var assemblyAttributes = type.Assembly.GetCustomAttributesData();
        var constraint = methodAttributes.FirstOrDefault(IsConstraint) ?? classAttributes.FirstOrDefault(IsConstraint)
            ?? assemblyAttributes.FirstOrDefault(IsConstraint);
        var names = new List<string>();
        if (constraint != null)
        {
            var keys = constraint.ConstructorArguments.SelectMany(argument =>
                argument.Value is IEnumerable<CustomAttributeTypedArgument> values
                    ? values.Select(value => value.Value?.ToString() ?? "")
                    : new[] { argument.Value?.ToString() ?? "" }).Where(key => key.Length > 0).ToArray();
            names.AddRange(keys.Length == 0 ? new[] { "exclusive" } : keys.Select(key => "key:" + key));
        }
        names.AddRange(classAttributes.Concat(methodAttributes).Concat(assemblyAttributes)
            .Where(attribute => attribute.AttributeType.IsGenericType && attribute.AttributeType.Name == "ParallelLimiterAttribute`1")
            .Select(attribute => "limiter:" + attribute.AttributeType.GenericTypeArguments[0].FullName));
        return names.Distinct().Order().ToArray();

        static bool IsConstraint(CustomAttributeData attribute) => attribute.AttributeType == typeof(NotInParallelAttribute);
    }

    [Before(TestSession)]
    public static void InitializeReactiveUi()
    {
        TaskItemViewModel.DefaultThrottleTime = TimeSpan.FromMilliseconds(10);

        var builder = RxAppBuilder.CreateReactiveUIBuilder();
        builder.WithCoreServices();
        builder.WithMainThreadScheduler(AvaloniaScheduler.Instance);
        App.ConfigureReactiveUIBuilder(builder);
        builder.BuildApp();
    }
}
