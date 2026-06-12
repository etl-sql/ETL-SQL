namespace ETL_SQL.ReportPortal.Models;

public record ParseDesignerRequest(string Script);

public record ParseDesignerResponse(DesignerStateDto DesignState, string? Error);

public record GenerateDesignerRequest(DesignerStateDto DesignState);

public record GenerateDesignerResponse(string Script);

public record DesignerStateDto(
    List<DesignerPageDto> Pages,
    List<DesignerDatasetDto> Datasets);

public record DesignerPageDto(
    string Id,
    string Name,
    string Mode,
    List<DesignerVisualDto> Visuals);

public record DesignerVisualDto(
    string Id,
    string Name,
    string Type,
    int GridCol,
    int GridRow,
    int GridColSpan,
    int GridRowSpan,
    string? Title,
    string? Dataset,
    Dictionary<string, string> Mappings,
    Dictionary<string, string> Options);

public record DesignerDatasetDto(
    string Id,
    string Name,
    string Query);

public record ScriptContentRequest(string ScriptText);

public record ScriptContentResponse(string ScriptText, long Version = 1);
