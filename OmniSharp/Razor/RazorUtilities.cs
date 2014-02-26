using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Mvc.Razor;
using System.Web.Razor;
using System.Web.Razor.Parser;
using Microsoft.CSharp;
using OmniSharp.Common;

namespace OmniSharp.Razor
{
    public class RazorUtilities
    {
        public bool IsRazor(Request request)
        {
            return request.FileName.EndsWith(".cshtml");
        }

        public CSharpConversionResult ConvertToCSharp(String fileName, String source)
        {
            var razorHost = new MvcWebPageRazorHost(fileName, "/")
            {
                DefaultDebugCompilation = true,
                DesignTimeMode = true,
            };

            var engine = new RazorTemplateEngine(razorHost);
            var output = engine.GenerateCode(new StringReader(source), null, null, fileName);
            var result = new CSharpConversionResult
            {
                Success = output.Success,
                OriginalSource = source,
            };
            if (output.Success)
            {
                var codeProvider = new CSharpCodeProvider();
                using (var codeStream = new MemoryStream())
                {
                    using(var writer = new StreamWriter(codeStream))
                    {
                        codeProvider.GenerateCodeFromCompileUnit(output.GeneratedCode, writer, new CodeGeneratorOptions());
                    }
                    result.Source = Encoding.UTF8.GetString(codeStream.ToArray());
                    result.Mappings = output.DesignTimeLineMappings;
                }
            }
            else
            {
                result.Errors = output.ParserErrors.Select(error => new Error
                    {
                        Message = error.Message.Replace("'", "''"),
                        Column = error.Location.CharacterIndex,
                        Line = error.Location.LineIndex,
                        FileName = fileName
                    }
                ).ToList();
            }
            return result;
        }
    }
}
