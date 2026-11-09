using System.Reflection;
using FluentResults;

namespace AqtCoreFW.SharedKernel.Application.Behaviors;

/// <summary>
/// Constructs FluentResults failure responses compatible with both Result and Result&lt;T&gt;
/// pipeline behavior return types. Direct cast (TResponse)(object)Result.Fail(...) fails
/// at runtime when TResponse is Result&lt;T&gt; because boxed Result cannot unbox to Result&lt;T&gt;.
/// </summary>
internal static class ResultBehaviorHelper
{
    internal static TResponse CreateFail<TResponse>(IEnumerable<IError> errors)
    {
        var errorList = errors.ToList();
        var type = typeof(TResponse);

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<>))
        {
            // Call Result.Fail<T>(IEnumerable<IError>) via reflection
            var valueType = type.GetGenericArguments()[0];
            var failMethod = typeof(Result)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "Fail"
                    && m.IsGenericMethodDefinition
                    && m.GetParameters() is [{ ParameterType: var pt }]
                    && pt == typeof(IEnumerable<IError>))
                .MakeGenericMethod(valueType);

            return (TResponse)failMethod.Invoke(null, [errorList])!;
        }

        return (TResponse)(object)Result.Fail(errorList);
    }
}
