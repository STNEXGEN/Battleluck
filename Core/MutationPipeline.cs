using System;
using System.Collections.Generic;

public sealed class MutationPipeline
{
    public sealed class StepResult
    {
        public string Name { get; }
        public bool Success { get; }
        public string? Error { get; }

        public StepResult(string name, bool success, string? error = null)
        {
            Name = name;
            Success = success;
            Error = error;
        }
    }

    public sealed class Context
    {
        readonly List<Func<StepResult>> _steps = new();
        readonly List<StepResult> _results = new();

        public IReadOnlyList<StepResult> Results => _results;

        public Context Step(string name, Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            _steps.Add(() =>
            {
                try
                {
                    action();
                    return new StepResult(name, success: true);
                }
                catch (Exception ex)
                {
                    return new StepResult(name, success: false, error: ex.Message);
                }
            });

            return this;
        }

        internal PipelineResult Execute(string pipelineName)
        {
            foreach (var step in _steps)
            {
                var result = step();
                _results.Add(result);

                if (!result.Success)
                {
                    return PipelineResult.Fail(
                        pipelineName,
                        result.Name,
                        result.Error ?? "Unknown error",
                        _results
                    );
                }
            }

            return PipelineResult.Ok(pipelineName, _results);
        }
    }

    public sealed class PipelineResult
    {
        public string Name { get; }
        public bool Success { get; }
        public string? FailedStep { get; }
        public string? Error { get; }
        public IReadOnlyList<StepResult> Steps { get; }

        PipelineResult(
            string name,
            bool success,
            string? failedStep,
            string? error,
            IReadOnlyList<StepResult> steps)
        {
            Name = name;
            Success = success;
            FailedStep = failedStep;
            Error = error;
            Steps = steps;
        }

        public static PipelineResult Ok(string name, IReadOnlyList<StepResult> steps) =>
            new(name, success: true, failedStep: null, error: null, steps: steps);

        public static PipelineResult Fail(
            string name,
            string failedStep,
            string error,
            IReadOnlyList<StepResult> steps) =>
            new(name, success: false, failedStep: failedStep, error: error, steps: steps);
    }

    public static PipelineResult Run(string name, Action<Context> configure)
    {
        if (configure == null) throw new ArgumentNullException(nameof(configure));

        var ctx = new Context();
        configure(ctx);
        return ctx.Execute(name);
    }
}
