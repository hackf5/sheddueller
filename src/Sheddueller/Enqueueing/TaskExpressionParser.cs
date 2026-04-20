namespace Sheddueller.Enqueueing;

using System.Linq.Expressions;

internal static class TaskExpressionParser
{
    public static ParsedTask Parse<TService, TResult>(Expression<Func<TService, CancellationToken, TResult>> work)
      => ParseCore<TService>(
        work,
        work.Parameters[0],
        work.Parameters[1],
        jobContextParameter: null);

    public static ParsedTask Parse<TService, TResult>(Expression<Func<TService, CancellationToken, IJobContext, TResult>> work)
      => ParseCore<TService>(
        work,
        work.Parameters[0],
        work.Parameters[1],
        work.Parameters[2]);

    private static ParsedTask ParseCore<TService>(
        LambdaExpression work,
        ParameterExpression serviceParameter,
        ParameterExpression cancellationTokenParameter,
        ParameterExpression? jobContextParameter)
    {
        var body = StripConvert(work.Body);

        if (body is not MethodCallExpression methodCall)
        {
            throw new ArgumentException("Submitted work must be a single service instance method call.", nameof(work));
        }

        ValidateTargetMethod<TService>(methodCall, serviceParameter);

        var methodParameters = methodCall.Method.GetParameters();
        if (methodParameters.Length != methodCall.Arguments.Count)
        {
            throw new ArgumentException("Submitted work must explicitly provide every target method argument.", nameof(work));
        }

        var serializableArguments = new List<object?>();
        var serializableParameterTypes = new List<Type>();
        var forwardedCancellationToken = false;
        var forwardedJobContext = false;

        for (var i = 0; i < methodParameters.Length; i++)
        {
            var parameter = methodParameters[i];
            var argument = methodCall.Arguments[i];

            if (parameter.ParameterType.IsByRef)
            {
                throw new ArgumentException("Submitted work cannot target methods with ref or out parameters.", nameof(work));
            }

            if (parameter.ParameterType == typeof(CancellationToken))
            {
                if (!IsSameParameter(argument, cancellationTokenParameter))
                {
                    throw new ArgumentException(
                      "CancellationToken target method parameters must receive the scheduler-owned cancellation token.",
                      nameof(work));
                }

                forwardedCancellationToken = true;
                continue;
            }

            if (parameter.ParameterType == typeof(IJobContext))
            {
                if (jobContextParameter is null || !IsSameParameter(argument, jobContextParameter))
                {
                    throw new ArgumentException(
                      "IJobContext target method parameters must receive the scheduler-owned job context.",
                      nameof(work));
                }

                forwardedJobContext = true;
                continue;
            }

            if (ReferencesParameter(argument, serviceParameter)
                || ReferencesParameter(argument, cancellationTokenParameter)
                || (jobContextParameter is not null && ReferencesParameter(argument, jobContextParameter)))
            {
                throw new ArgumentException("Only the target service instance, scheduler cancellation token, and job context may be runtime-bound.", nameof(work));
            }

            ValidateSerializableParameterType(parameter.ParameterType, nameof(work));
            var value = EvaluateArgument(argument);
            ValidateSerializableArgumentValue(value, nameof(work));

            serializableArguments.Add(value);
            serializableParameterTypes.Add(parameter.ParameterType);
        }

        if (!forwardedCancellationToken)
        {
            throw new ArgumentException("Submitted work must forward the scheduler-owned CancellationToken.", nameof(work));
        }

        if (jobContextParameter is not null && !forwardedJobContext)
        {
            throw new ArgumentException("Submitted work must forward the scheduler-owned IJobContext.", nameof(work));
        }

        return new ParsedTask(
          methodCall.Method.Name,
          [.. methodParameters.Select(parameter => TypeNameFormatter.Format(parameter.ParameterType))],
          serializableArguments,
          serializableParameterTypes);
    }

    private static void ValidateTargetMethod<TService>(MethodCallExpression methodCall, ParameterExpression serviceParameter)
    {
        if (methodCall.Method.IsStatic || methodCall.Object is null)
        {
            throw new ArgumentException("Submitted work must target an instance method on the submitted service type.");
        }

        if (!ReferencesParameter(methodCall.Object, serviceParameter))
        {
            throw new ArgumentException("Submitted work must call a method on the submitted service parameter.");
        }

        if (methodCall.Method.IsGenericMethod || methodCall.Method.ContainsGenericParameters)
        {
            throw new ArgumentException("Submitted work cannot target generic methods.");
        }

        if (!typeof(TService).IsAssignableTo(methodCall.Object.Type) && !methodCall.Object.Type.IsAssignableTo(typeof(TService)))
        {
            throw new ArgumentException("Submitted work must target the submitted service type.");
        }

        if (methodCall.Method.ReturnType != typeof(Task) && methodCall.Method.ReturnType != typeof(ValueTask))
        {
            throw new ArgumentException("Submitted work must target a method returning Task or ValueTask.");
        }
    }

    private static object? EvaluateArgument(Expression argument)
    {
        var converted = Expression.Convert(argument, typeof(object));
        var lambda = Expression.Lambda<Func<object?>>(converted);

        return lambda.Compile().Invoke();
    }

    private static Expression StripConvert(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
        {
            expression = unary.Operand;
        }

        return expression;
    }

    private static bool ReferencesParameter(Expression expression, ParameterExpression parameter)
    {
        var visitor = new ParameterReferenceVisitor(parameter);
        visitor.Visit(expression);

        return visitor.Found;
    }

    private static bool IsSameParameter(Expression expression, ParameterExpression parameter)
      => ReferenceEquals(StripConvert(expression), parameter);

    private static void ValidateSerializableParameterType(Type type, string parameterName)
    {
        if (typeof(CancellationToken).IsAssignableFrom(type)
            || typeof(IJobContext).IsAssignableFrom(type)
            || typeof(Delegate).IsAssignableFrom(type)
            || typeof(Stream).IsAssignableFrom(type))
        {
            throw new ArgumentException($"Parameter type '{type}' is not supported for serialized task arguments.", parameterName);
        }
    }

    private static void ValidateSerializableArgumentValue(object? value, string parameterName)
    {
        if (value is CancellationToken or IJobContext or Delegate or Stream)
        {
            throw new ArgumentException($"Argument value of type '{value.GetType()}' is not supported for serialized task arguments.", parameterName);
        }
    }

    private sealed class ParameterReferenceVisitor(ParameterExpression parameter) : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node == parameter)
            {
                this.Found = true;
            }

            return node;
        }
    }
}
