using System;
using SubwaySurfers.Player.Contracts;

namespace SubwaySurfers.Player.Domain
{
    public sealed class ForwardSpeedApi : IForwardSpeedApi
    {
        private const string DiagnosticField = "ForwardSpeed";
        private readonly Action<ValidationDiagnostic> diagnosticSink;
        private float forwardSpeed;

        public ForwardSpeedApi(
            float initialSpeed,
            Action<ValidationDiagnostic> diagnosticSink)
        {
            if (!IsFinite(initialSpeed) || initialSpeed < 0f)
                throw new ArgumentOutOfRangeException(nameof(initialSpeed));

            forwardSpeed = initialSpeed;
            this.diagnosticSink = diagnosticSink;
        }

        public float ForwardSpeed { get { return forwardSpeed; } }

        public SpeedSetResult SetForwardSpeed(float requestedSpeed)
        {
            if (IsFinite(requestedSpeed) && requestedSpeed >= 0f)
            {
                forwardSpeed = requestedSpeed;
                return Result(CommandStatus.Accepted, RejectionReason.None, requestedSpeed);
            }

            ReportInvalid(requestedSpeed);
            return Result(CommandStatus.Rejected, RejectionReason.InvalidValue, requestedSpeed);
        }

        private SpeedSetResult Result(
            CommandStatus status,
            RejectionReason reason,
            float requestedSpeed)
        {
            return new SpeedSetResult(
                status,
                PlayerCommandKind.SetForwardSpeed,
                reason,
                PlayerState.Running,
                requestedSpeed,
                forwardSpeed);
        }
        private void ReportInvalid(float value)
        {
            if (diagnosticSink == null) return;

            diagnosticSink(new ValidationDiagnostic(
                DiagnosticSeverity.Error,
                DiagnosticCode.InvalidValue,
                DiagnosticField,
                "ForwardSpeed rejected a " + InvalidCategory(value) + " value."));
        }

        private static string InvalidCategory(float value)
        {
            if (float.IsNaN(value)) return "NaN";
            if (float.IsPositiveInfinity(value)) return "positive infinity";
            if (float.IsNegativeInfinity(value)) return "negative infinity";
            return "negative";
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
