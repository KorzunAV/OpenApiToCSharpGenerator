using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using Newtonsoft.Json;

namespace OpenApiToCSharpGenerator.Common
{
    public class OpenApiGenerator
    {
        private readonly IAppSettings _settings;
        private InternalVariables _internalVariables;

        public OpenApiGenerator(IAppSettings settings)
        {
            _settings = settings;
        }


        public async Task Run()
        {
            try
            {
                ReadInternalVariables();

                var httpClient = new HttpClient();
                var stream = await httpClient.GetStreamAsync(_settings.UrlToOpenApi);
                var openApiDocument = new OpenApiStreamReader().Read(stream, out var diagnostic);

                CreateComponents(openApiDocument);
                CreateApi(openApiDocument);
                CreateApiTest(openApiDocument);
            }
            catch (Exception e)
            {
                var t = e.Message;
            }
        }

        private void ReadInternalVariables()
        {
            var json = File.ReadAllText(Path.Combine("Templates", _settings.Template.ToString(), "InternalVariables.json"));
            _internalVariables = JsonConvert.DeserializeObject<InternalVariables>(json);
        }

        private void CreateApi(OpenApiDocument api)
        {
            var apiFunctionTemplate = File.ReadAllText(Path.Combine("Templates", _settings.Template.ToString(), "ApiFunctionTemplate.txt"));
            var apiFuncList = new List<string>();
            foreach (var openApiPath in api.Paths)
            {
                foreach (var apiOperation in openApiPath.Value.Operations)
                {
                    var requestName = CreateRequestClass(openApiPath, apiOperation);
                    var respType = GetResponseClass(_settings.ApiName, apiOperation);

                    var fName = GetFunctionName(openApiPath, apiOperation);
                    var url = ToUrl(openApiPath, apiOperation);
                    var apiFunctionFile = Transform(apiFunctionTemplate, apiOperation.Value)
                        .Replace("[@ResponseType]", respType)
                        .Replace("[@FunctionName]", fName)
                        .Replace("[@RequestName]", requestName)
                        .Replace("[@UrlPart]", url)
                        .Replace("[@RequestType]", apiOperation.Key.ToString());
                    apiFuncList.Add(apiFunctionFile);
                }
            }

            var space = new string(' ', 4);
            var apiTemplate = File.ReadAllText(Path.Combine("Templates", _settings.Template.ToString(), "ApiTemplate.txt"));
            var apiFile = Transform(apiTemplate, null, null, api)
                .Replace("[@ApiFunctions]", string.Join($"{Environment.NewLine}{space}{Environment.NewLine}", apiFuncList));

            SaveToFile(apiFile, _settings.PahtToGeneratedApi, _settings.ApiName);
        }

        private string GetFunctionName(KeyValuePair<string, OpenApiPathItem> openApiPath, KeyValuePair<OperationType, OpenApiOperation> apiOperation)
        {
            if (!string.IsNullOrEmpty(apiOperation.Value.OperationId))
                return apiOperation.Value.OperationId;

            var parts = openApiPath.Key.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            var pathParts = new string[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part.StartsWith("{") && part.EndsWith("}"))
                    part = $"By_{part.Substring(1, part.Length - 2)}";

                if (part.IndexOf('{') != -1 && part.IndexOf('}') != -1)
                    throw new NotImplementedException();

                pathParts[i] = part;
            }

            var nameMainPart = string.Join("_", pathParts);
            var name = ToName(nameMainPart + apiOperation.Key);
            return name;
        }

        private void CreateApiTest(OpenApiDocument api)
        {
            var apiTestFunctionTemplate = File.ReadAllText(Path.Combine("Templates", _settings.Template.ToString(), "ApiTestFunctionTemplate.txt"));
            var apiTestFuncList = new List<string>();
            var requests = new List<string>();
            foreach (var openApiPath in api.Paths)
            {
                foreach (var apiOperation in openApiPath.Value.Operations)
                {
                    var requestName = CreateRequestClass(openApiPath, apiOperation);
                    var apiTestFunctionFile = Transform(apiTestFunctionTemplate, apiOperation.Value)
                        .Replace("[@RequestName]", requestName);
                    apiTestFuncList.Add(apiTestFunctionFile);
                    requests.Add($"private {requestName}Request {requestName}Request = null;");
                }
            }

            var space = new string(' ', 4);
            var apiTestTemplate = File.ReadAllText(Path.Combine("Templates", _settings.Template.ToString(), "ApiTestTemplate.txt"));
            var apiTestFile = Transform(apiTestTemplate)
                .Replace("[@ApiTestRequests]", string.Join($"{Environment.NewLine}{space}{space}", requests))
                .Replace("[@ApiTestFunctions]", string.Join($"{Environment.NewLine}{Environment.NewLine}", apiTestFuncList));

            SaveToFile(apiTestFile, _settings.PathToGeneratedTests, $"{_settings.ApiName}Test");
        }

        private void CreateComponents(OpenApiDocument api)
        {
            if (api.Components == null)
                return;

            foreach (var schema in api.Components.Schemas)
            {
                CreateComponent(schema);
            }
        }

        private void CreateComponent(KeyValuePair<string, OpenApiSchema> schema)
        {
            var required = schema.Value.Required;
            var properties = new List<string>();
            var space = new string(' ', 8);
            var componentItemTemplate = File.ReadAllText(Path.Combine("Templates", _settings.Template.ToString(), "ComponentItemTemplate.txt"));

            foreach (var property in schema.Value.Properties)
            {
                var pNameOrigin = property.Key;
                var pType = ToType(property);

                var componentItem = Transform(componentItemTemplate, null, property.Value)
                    .Replace($"[@{nameof(InternalVariables.OnRequired)}]", (required != null && required.Contains(pNameOrigin)) ? _internalVariables.OnRequired : string.Empty)
                    .Replace("[@PropertyNameOrigin]", pNameOrigin)
                    .Replace("[@PropertyName]", ToName(pNameOrigin))
                    .Replace("[@PropertyType]", pType);

                properties.Add(componentItem);
            }

            var fullName = schema.Key;

            string className;
            string nsp;
            string path;

            if (schema.Value.Reference.IsLocal)
            {
                nsp = $"{_settings.ProjectName}.{_settings.SubProjectName}.Models";
                className = $"{fullName}Model";
                path = _settings.PathToModels;
            }
            else
            {
                var i = fullName.LastIndexOf('.');
                nsp = fullName.Remove(i);
                className = fullName.Remove(0, i + 1);
                path = Path.Combine(_settings.PahtToGeneratedComponents, nsp);
            }

            var componentTemplate = File.ReadAllText(Path.Combine("Templates", _settings.Template.ToString(), "ComponentTemplate.txt"));
            var componentFile = Transform(componentTemplate, null, schema.Value)
                .Replace("[@Namespace]", nsp)
                .Replace("[@ClassName]", className)
                .Replace("[@Fields]", string.Join($"{Environment.NewLine}{space}{Environment.NewLine}", properties));

            SaveToFile(componentFile, path, className);
        }

        private string ToType(KeyValuePair<string, OpenApiSchema> property)
        {
            return ToType(property.Value, property.Key);
        }

        private string CreateRequestClass(KeyValuePair<string, OpenApiPathItem> openApiPath, KeyValuePair<OperationType, OpenApiOperation> apiOperation)
        {
            var sb = new StringBuilder();
            var properties = new List<string>();
            if (apiOperation.Value.Parameters != null)
            {
                foreach (var apiParameter in apiOperation.Value.Parameters)
                {
                    try
                    {
                        var description = apiParameter.Description;
                        var required = apiParameter.Required;

                        switch (apiParameter.In)
                        {
                            //case "body":
                            //    {
                            //        sb.AppendLine("        [Body]");
                            //        break;
                            //    }
                            case ParameterLocation.Query:
                            {
                                sb.AppendLine("        [Query]");
                                break;
                            }
                            case ParameterLocation.Path:
                            {
                                sb.AppendLine("        [Path]");
                                break;
                            }
                            //case "formData":
                            //    {
                            //        //sb.AppendLine("        [Query]");
                            //        break;
                            //    }
                            default:
                            {
                                throw new NotImplementedException();
                            }
                        }

                        sb.AppendLine($"        [JsonProperty(\"{apiParameter.Name}\")]");
                        if (required)
                            sb.AppendLine("        [JsonRequired]");

                        var pName = ToName(apiParameter.Name);
                        sb.AppendLine($"        public {ToType(apiParameter.Schema, pName)} {pName} {{ get; set; }}");
                        sb.AppendLine();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        throw;
                    }
                }

                if (apiOperation.Value.RequestBody != null)
                {
                    foreach (var kvContent in apiOperation.Value.RequestBody.Content)
                    {
                        switch (kvContent.Key)
                        {
                            case "multipart/form-data":
                            {
                                foreach (var schemaProperty in kvContent.Value.Schema.Properties)
                                {
                                    if (kvContent.Value.Schema.Required.Contains(schemaProperty.Key))
                                        sb.AppendLine("        [JsonRequired]");

                                    sb.AppendLine($"        [JsonProperty(\"{schemaProperty.Key}\")]");
                                    var pName = ToName(schemaProperty.Key);
                                    sb.AppendLine($"        public {ToType(schemaProperty.Value, pName)} {pName} {{ get; set; }}");
                                    sb.AppendLine();
                                }


                                break;
                            }
                            case "application/json":
                            {
                                sb.AppendLine("        [Body]");
                                var pName = "RequestBody";
                                sb.AppendLine($"        public {ToType(kvContent.Value.Schema, pName)} {pName} {{ get; set; }}");
                                sb.AppendLine();

                                break;
                            }
                        }
                    }
                }
            }

            var space = new string(' ', 8);

            var requestName = GetFunctionName(openApiPath, apiOperation);
            var requestModelTemplate = File.ReadAllText(Path.Combine("Templates", _settings.Template.ToString(), "RequestModelTemplate.txt"));
            var requestModelFile = Transform(requestModelTemplate)
                .Replace("[@RequestName]", requestName)
                .Replace("[@Fields]", sb.ToString());
            // .Replace("@Fields", string.Join($"{Environment.NewLine}{space}{Environment.NewLine}", properties));

            SaveToFile(requestModelFile, _settings.PathToRequests, requestName);
            return requestName;
        }

        private string GetResponseClass(string apiName, KeyValuePair<OperationType, OpenApiOperation> kv)
        {
            if (kv.Value.Responses.ContainsKey("200"))
            {
                var apiResponse = kv.Value.Responses["200"];
                if (apiResponse == null || apiResponse.Content.Count == 0)
                    return "VoidResponse";

                return ToType(apiResponse.Content["application/json"].Schema, null);
            }
            if (kv.Value.Responses.ContainsKey("201"))
            {
                var apiResponse = kv.Value.Responses["201"];
                if (apiResponse == null || apiResponse.Content.Count == 0)
                    return "VoidResponse";

                return ToType(apiResponse.Content["application/json"].Schema, null);
            }

            if (kv.Value.Responses.ContainsKey("204"))
                return "VoidResponse";

            throw new NotImplementedException("GetResponseClass");
        }

        private string ToName(string name, bool firstUpper = true, bool replaceNums = false)
        {
            var sb = new StringBuilder(name);
            for (var i = 0; i < sb.Length; i++)
            {
                if (i == 0 && firstUpper)
                    sb[i] = char.ToUpper(sb[i]);

                if ((sb[i] == '_' || sb[i] == '.') && i + 1 < sb.Length)
                    sb[i + 1] = char.ToUpper(sb[i + 1]);
            }
            sb.Replace("_", string.Empty).Replace(".", string.Empty);
            var rez = sb.ToString();
            if (rez.Equals("params"))
                rez = "parameters";

            if (replaceNums)
            {
                rez = rez
                    .Replace("1", "One")
                    .Replace("2", "Two")
                    .Replace("3", "Three")
                    .Replace("4", "Four")
                    .Replace("5", "Five")
                    .Replace("6", "Six")
                    .Replace("7", "Seven")
                    .Replace("8", "Eight")
                    .Replace("9", "Nine");
            }

            return rez;
        }

        private string ToType(OpenApiSchema schema, string pName)
        {
            if (schema.Enum != null && schema.Enum.Any())
            {
                if (schema.Enum?.Any() == true)
                {
                    AddEnum(schema, pName);

                    if (schema.Type == "array")
                        return $"{ToName(pName)}Enum[]";

                    return $"{ToName(pName)}Enum";
                }
            }

            switch (schema.Type)
            {
                case "integer" when schema.Format == "int64":
                    return "long?";
                case "integer" when schema.Format == "int32":
                    return "int?";
                case "integer":
                    return "int?";
                case "boolean":
                    return "bool?";
                case "string":
                    return "string";
                case "file":
                    return "IFormFile";
                case "object" when schema.Reference != null:
                {
                    if (schema.Reference.IsLocal)
                    {
                        return $"{schema.Reference.Id}Model";
                    }

                    return schema.Reference.Id;
                }
                case "object":
                {
                    return "object";
                }
                case "number" when schema.Format == "double":
                    return "double?";
                case "number":
                    return "decimal?";
                case "array":
                {
                    if (schema.Items == null)
                    {
                        throw new NotImplementedException();
                    }
                    return $"{ToType(schema.Items, pName)}[]";
                }
                default:
                {
                    //if (string.IsNullOrEmpty(pType))
                    //{
                    //    {
                    //        var reft = jToken.GetValueOrDefault<string>("$ref");
                    //        if (!string.IsNullOrEmpty(reft))
                    //            return RefToName(reft);
                    //    }

                    //    var schema = jToken.GetValueOrDefault<JToken>("schema");
                    //    if (schema != null)
                    //    {
                    //        var reft = schema.GetValueOrDefault<string>("$ref");
                    //        return RefToName(reft);
                    //    }
                    //}
                    //throw new NotImplementedException();
                    return "NotImplemented";
                }
            }
        }

        private void AddEnum(OpenApiSchema schema, string pName)
        {
            var enumItemTemplate = File.ReadAllText(Path.Combine("Templates", _settings.Template.ToString(), "EnumItemTemplate.txt"));

            var enumFields = new List<string>();
            for (var i = 0; i < schema.Enum.Count; i++)
            {
                var field = schema.Enum[i];
                switch (field)
                {
                    case Microsoft.OpenApi.Any.OpenApiString typed:
                    {
                        var enumItem = Transform(enumItemTemplate)
                            .Replace("[@Key]", ToName(typed.Value, true, true))
                            .Replace("[@KeyOriginal]", typed.Value)
                            .Replace("[@Value]", i.ToString());
                        enumFields.Add(enumItem);
                        break;
                    }
                    default:
                    {
                        throw new NotImplementedException();
                    }
                }
            }

            var enumTemplate = File.ReadAllText(Path.Combine("Templates", _settings.Template.ToString(), "EnumTemplate.txt"));
            var enumFile = Transform(enumTemplate, null, schema)
                .Replace("[@EnumName]", ToName(pName))
                .Replace("[@EnumFields]", string.Join($"{Environment.NewLine}{Environment.NewLine}", enumFields));

            SaveToFile(enumFile, _settings.PathToGeneratedEnums, $"{ToName(pName)}Enums");
        }

        private void SaveToFile(string file, string dirPath, string className)
        {
            //var dirPath = Path.Combine(TestContext.CurrentContext.TestDirectory, dir);
            var filePath = Path.Combine(dirPath, $"{className}.generated.cs");
            var t = Path.GetFullPath(filePath);

            if (!Directory.Exists(dirPath))
                Directory.CreateDirectory(dirPath);

            File.WriteAllText(filePath, file);
        }

        private void SaveToFile(StringBuilder sb, string dirPath, string className)
        {
            SaveToFile(sb.ToString(), dirPath, className);
        }

        private string GetSummary(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var arr = text.Split(new[] { Environment.NewLine, "\r\n", "\n\r", "\n", "\r" }, StringSplitOptions.None);
            var summ = string.Join(_internalVariables.SummaryNewLine, arr.Select(t => t.Trim()));
            return summ;
        }

        private string ToUrl(in KeyValuePair<string, OpenApiPathItem> openApiPath, in KeyValuePair<OperationType, OpenApiOperation> apiOperation)
        {
            var outStr = openApiPath.Key;

            foreach (var apiParameter in apiOperation.Value.Parameters)
            {
                if (apiParameter.In == ParameterLocation.Path)
                {
                    outStr = outStr.Replace($"{{{apiParameter.Name}}}", $"{{args.{ToName(apiParameter.Name)}}}");
                }
            }
            return outStr;
        }

        private string Transform(string file, OpenApiOperation openApiOperation = null, OpenApiSchema schema = null, OpenApiDocument api = null)
        {
            file = file
                .Replace($"[@{nameof(_settings.ProjectName)}]", _settings.ProjectName)
                .Replace($"[@{nameof(_settings.SubProjectName)}]", _settings.SubProjectName)
                .Replace($"[@{nameof(_settings.ApiName)}]", _settings.ApiName);

            if (openApiOperation != null)
            {
                file = file
                    .Replace($"[@{nameof(_internalVariables.OnDeprecated)}]", openApiOperation.Deprecated ? _internalVariables.OnDeprecated : string.Empty)
                    .Replace($"[@{nameof(openApiOperation.Description)}]", GetSummary(openApiOperation.Description));
            }
            if (schema != null)
            {
                file = file
                    .Replace($"[@{nameof(_internalVariables.OnDeprecated)}]", schema.Deprecated ? _internalVariables.OnDeprecated : string.Empty)
                    .Replace($"[@{nameof(schema.Description)}]", GetSummary(schema.Description))
                    .Replace($"[@{nameof(_internalVariables.OnNullableProperty)}]", schema.Nullable ? _internalVariables.OnNullableProperty : string.Empty);
            }
            //if (api != null)
            //{
            //    file = file
            //    .Replace("[@Description]", api.Description);
            //}

            return file;
        }
    }
}