namespace soccer_gpt_application.Models;

/// <summary>
/// Context flags for match evaluation
/// </summary>
public class ContextFlags
{
    public bool IsDerby { get; set; }
    public bool HasEuropeanGameWithin3Days { get; set; }
}
