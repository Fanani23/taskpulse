namespace TaskPulse.Api.Models;

public static class TaskLimits
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 2000;
    public const int SearchMaxLength = 100;
    public const int StatsMinDays = 1;
    public const int StatsMaxDays = 90;
    public const int StatsDefaultDays = 14;
    public const int LabelsMax = 10;
    public const int LabelMaxLength = 32;
    public const int AssigneeMaxLength = 200;
}
