using System.Collections.Generic;
using System.Text.Json;

namespace soccer_gpt_application.Models;

public class DatabaseSchemaDto
{
    public List<DatabaseTableDto> Tables { get; set; } = new();
}

public class DatabaseTableDto
{
    public string TableName { get; set; } = string.Empty;
    public long RecordCount { get; set; }
    public List<DatabaseColumnDto> Columns { get; set; } = new();
    public List<Dictionary<string, object>> Records { get; set; } = new();
}

public class DatabaseColumnDto
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}
