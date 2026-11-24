namespace PerceptAI.API.Settings
{
    
    public class SupabaseSettings
    {
        public const string SectionName = "SupabaseSettings";

        public string Url { get; set; } = string.Empty;

        public string ServiceRoleKey { get; set; } = string.Empty;
    }
}
