using System;
using OpenApiToCSharpGenerator.Common;

namespace OpenApiToCSharpGenerator
{
    public sealed partial class Settings : IAppSettings
    {
        TemplateType IAppSettings.Template
        {
            get => string.IsNullOrEmpty(Template) ? TemplateType.CSharp : Enum.Parse<TemplateType>(Template);
            set => Template = value.ToString();
        }
    }
}
