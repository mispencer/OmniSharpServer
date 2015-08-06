using System;
using System.Collections.Generic;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Razor;
using Microsoft.CSharp;
using OmniSharp.Common;
using System.Web.Configuration;
using System.Web.WebPages.Razor;
using System.Web.WebPages.Razor.Configuration;
using OmniSharp.Solution;
using System.Reflection;
using System.Web.Razor.Generator;
using System.Web.Razor.Parser.SyntaxTree;
using ICSharpCode.NRefactory.TypeSystem.Implementation;

namespace OmniSharp.Razor
{
    public class RazorUtilities
    {
        public bool IsRazor(Request request)
        {
            return request.FileName.EndsWith(".cshtml");
        }

        public CSharpConversionResult ConvertToCSharp(IProject project, String fileName, String source)
        {
            var engine = GetRazorHost(project, fileName);
            var output = engine.GenerateCode(new StringReader(source), null, null, fileName);
            var mappings = new Dictionary<int,GeneratedCodeMapping>();
            var parserErrors = new List<RazorError>();
            foreach(var error in output.ParserErrors) {
                parserErrors.Add(new RazorError(error.Message, error.Location.AbsoluteIndex, error.Location.LineIndex, error.Location.CharacterIndex, error.Length));
            }
            foreach(var item in output.DesignTimeLineMappings) {
                var i = item.Value;
                if (i.StartOffset == null) {
                    mappings[item.Key] = new GeneratedCodeMapping(i.StartLine, i.StartColumn, i.StartGeneratedColumn, i.CodeLength);
                } else {
                    mappings[item.Key] = new GeneratedCodeMapping(i.StartOffset, i.StartLine, i.StartColumn, i.StartGeneratedColumn, i.CodeLength);
                }
            }

            var result = new CSharpConversionResult
            {
                Success = output.Success,
                OriginalSource = source,
            };
            if (output.Success && !parserErrors.Any() && mappings != null)
            {
                var codeProvider = new CSharpCodeProvider();
                using (var codeStream = new MemoryStream())
                {
                    using(var writer = new StreamWriter(codeStream))
                    {
                        codeProvider.GenerateCodeFromCompileUnit(output.GeneratedCode, writer, new CodeGeneratorOptions());
                    }
                    result.Source = Encoding.UTF8.GetString(codeStream.ToArray()).Replace("@__RazorDesignTimeHelpers__()", " __RazorDesignTimeHelpers__()");
                    result.Mappings = mappings;
                }
                //Console.WriteLine("Source: "+result.Source);
            }
            else
            {
                result.Errors = parserErrors.Select(error => new Error
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

        private static dynamic GetRazorHost(IProject project, string fileName)
        {
            RazorWebSectionGroup razorConfigSection;
            var config = OpenConfigFile(fileName);
            try
            {
                var host = config.GetSection(HostSection.SectionName);
                var pages = config.GetSection(RazorPagesSection.SectionName);
                if (host is HostSection && pages is RazorPagesSection) {
                    // Okay
                } else {
                    host = new HostSection {
                        FactoryType = host.GetType().GetProperty("FactoryType").GetValue(host) as String,
                    };
                    pages = new RazorPagesSection {
                        Namespaces = pages.GetType().GetProperty("Namespaces").GetValue(pages) as NamespaceCollection,
                        PageBaseType = pages.GetType().GetProperty("PageBaseType").GetValue(pages) as String,
                    };
                }
                razorConfigSection = new RazorWebSectionGroup
                {
                    Host = (HostSection)host,
                    Pages = (RazorPagesSection)pages,
                };
            }
            catch (Exception)
            {
                // Couldn't get the configuration
                razorConfigSection = null;
                throw;
            }

            dynamic razorHost;
            if (razorConfigSection != null && !String.IsNullOrWhiteSpace(razorConfigSection.Host.FactoryType)) {
                var typeParts = razorConfigSection.Host.FactoryType.Split(',').Select(i => i.Trim());
                var factoryTypeName = typeParts.First();
                var hostAssemblyName = typeParts.Skip(1).First();
                var hostAssemblyReference = project.References.OfType<DefaultUnresolvedAssembly>().Single(i => i.AssemblyName == hostAssemblyName);
                var hostAssembly = Assembly.LoadFrom(hostAssemblyReference.Location);
                razorConfigSection.Host.FactoryType = String.Join(", ", factoryTypeName, hostAssembly.FullName);

                var razorAssemblyLocation = project.References.OfType<DefaultUnresolvedAssembly>().Single(i => i.AssemblyName == "System.Web.WebPages.Razor");
                var razorAssembly = Assembly.LoadFrom(razorAssemblyLocation.Location);
                var razorEngineAssemblyLocation = project.References.OfType<DefaultUnresolvedAssembly>().Single(i => i.AssemblyName == "System.Web.Razor");
                var razorEngineAssembly = Assembly.LoadFrom(razorEngineAssemblyLocation.Location);
                var factoryType = hostAssembly.GetType(factoryTypeName);

                dynamic configSection = razorAssembly.CreateInstance("System.Web.WebPages.Razor.Configuration.RazorWebSectionGroup");
                dynamic hostSection = razorAssembly.CreateInstance("System.Web.WebPages.Razor.Configuration.HostSection");
                dynamic pagesSection = razorAssembly.CreateInstance("System.Web.WebPages.Razor.Configuration.RazorPagesSection");
                hostSection.FactoryType = razorConfigSection.Host.FactoryType;
                pagesSection.Namespaces = razorConfigSection.Pages.Namespaces;
                pagesSection.PageBaseType = razorConfigSection.Pages.PageBaseType;
                configSection.Host = hostSection;
                configSection.Pages = pagesSection;
                var method = factoryType.GetMethod("CreateHostFromConfig", BindingFlags.Public|BindingFlags.Static, null, new[] { (Type)configSection.GetType(), typeof(String), typeof(String) }, null);
                if (method == null) {
                    method = factoryType.GetMethod("CreateHost", BindingFlags.Public|BindingFlags.Instance, null, new[] { typeof(String), typeof(String) }, null);
                    var razorFactory = hostAssembly.CreateInstance(factoryTypeName);
                    razorHost = method.Invoke(razorFactory, new Object[] { "/", fileName+"" });
                } else {
                    razorHost = method.Invoke(null, new Object[] { configSection, "/", fileName });
                }

                razorHost.DefaultDebugCompilation = true;
                razorHost.DesignTimeMode = true;
                var engineType = razorEngineAssembly.GetType("System.Web.Razor.RazorTemplateEngine");
                var constructor = engineType.GetConstructor(new Type[] { razorHost.GetType() });
                return constructor.Invoke(new object[] { razorHost });
            } else {
                razorHost
                = (razorConfigSection != null)
                ? WebRazorHostFactory.CreateHostFromConfig(razorConfigSection, "/", fileName)
                : WebRazorHostFactory.CreateDefaultHost("/", fileName)
                ;
                razorHost.DefaultDebugCompilation = true;
                razorHost.DesignTimeMode = true;
                return new RazorTemplateEngine(GetRazorHost(project, fileName));
            }
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
