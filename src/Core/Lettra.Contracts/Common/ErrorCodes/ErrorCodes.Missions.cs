namespace Lettra.Contracts.Common;

/// <summary>Codes d'erreur : lettre de mission, honoraires et temps passé.</summary>
public static partial class ErrorCodes
{
    public static class Mission
    {
        public const string NotFound = "Mission.NotFound";
        public const string KindInvalid = "Mission.KindInvalid";
        public const string ServicesInvalid = "Mission.ServicesInvalid";
        public const string FeeInvalid = "Mission.FeeInvalid";
        public const string ScheduleInvalid = "Mission.ScheduleInvalid";
        public const string RateInvalid = "Mission.RateInvalid";
        public const string SignatoryRequired = "Mission.SignatoryRequired";
        public const string NameTooLong = "Mission.NameTooLong";
        public const string DurationInvalid = "Mission.DurationInvalid";
        public const string TaskInvalid = "Mission.TaskInvalid";
        public const string DateInFuture = "Mission.DateInFuture";
        public const string NoteTooLong = "Mission.NoteTooLong";
        public const string TimeEntryNotFound = "Mission.TimeEntryNotFound";
        public const string TimeEntryNotYours = "Mission.TimeEntryNotYours";
        public const string DueDiligenceRequired = "Mission.DueDiligenceRequired";
    }
}
