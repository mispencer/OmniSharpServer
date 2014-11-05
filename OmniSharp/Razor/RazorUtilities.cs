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
using System.Web.Configuration;
using System.Web.WebPages.Razor;
using System.Web.WebPages.Razor.Configuration;

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
            var engine = new RazorTemplateEngine(GetRazorHost(fileName));
            var output = engine.GenerateCode(new StringReader(source), null, null, fileName);
            var result = new CSharpConversionResult
            {
                Success = output.Success,
                OriginalSource = source,
            };
            if (output.Success && !output.ParserErrors.Any() && output.DesignTimeLineMappings != null)
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

        private static WebPageRazorHost GetRazorHost(string fileName)
        {
            RazorWebSectionGroup razorConfigSection;
            try
            {
                var config = OpenConfigFile(fileName);
                razorConfigSection = new RazorWebSectionGroup
                {
                    Host = (HostSection)config.GetSection(HostSection.SectionName),
                    Pages = (RazorPagesSection)config.GetSection(RazorPagesSection.SectionName),
                };
            }
            catch (Exception)
            {
                // Couldn't get the configuration
                razorConfigSection = null;
            }

            WebPageRazorHost razorHost
                = (razorConfigSection != null)
                ? WebRazorHostFactory.CreateHostFromConfig(razorConfigSection, "/", fileName)
                : WebRazorHostFactory.CreateDefaultHost("/", fileName)
                ;
            razorHost.DefaultDebugCompilation = true;
            razorHost.DesignTimeMode = true;

            return razorHost;
        }

        private static System.Configuration.Configuration OpenConfigFile(string path)
        {
            var configFile = FindConfigFile(path);
            var vdm = new VirtualDirectoryMapping(configFile.DirectoryName, true, configFile.Name);
            var wcfm = new WebConfigurationFileMap();
            wcfm.VirtualDirectories.Add("/", vdm);
            return WebConfigurationManager.OpenMappedWebConfiguration(wcfm, "/");
        }

        private static FileInfo FindConfigFile(string path)
        {
            var file = new FileInfo(path);
            var dir = file.Directory;
            while(dir != null)
            {
                foreach(var subfile in dir.EnumerateFiles("Web.config"))
                {
                    return subfile;
                }
                dir = dir.Parent;
            }
            throw new Exception("Could not find config file");
        }
    }
}
