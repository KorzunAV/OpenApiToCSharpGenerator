namespace OpenApiToCSharpGenerator.Common
{
    public interface IAppSettings
    {
        string UrlToOpenApi { get; set; }
        string ApiName { get; set; }
        string ProjectName { get; set; }
        string SubProjectName { get; set; }
        string PahtToGeneratedApi { get; set; }
        string PahtToGeneratedComponents { get; set; }
        string PathToRequests { get; set; }
        string PathToGeneratedTests { get; set; }
        string PathToGeneratedEnums { get; set; }
        string PathToModels { get; set; }
        string PathToResponses { get; set; }
        TemplateType Template { get; set; }
    }

    public enum TemplateType
    {
        CSharp
    }
}