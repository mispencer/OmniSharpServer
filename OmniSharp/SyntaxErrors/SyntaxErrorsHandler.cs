using System.Linq;
using ICSharpCode.NRefactory.CSharp;
using OmniSharp.Common;
using OmniSharp.Razor;
using OmniSharp.Solution;
using OmniSharp.Parser;
using OmniSharp.Configuration;

namespace OmniSharp.SyntaxErrors
{
    public class SyntaxErrorsHandler
    {
		private readonly ISolution _solution;
		public SyntaxErrorsHandler(ISolution solution)
		{
			_solution = solution;
		}

        public SyntaxErrorsResponse FindSyntaxErrors(Request request)
        {
            var parser = new CSharpParser ();
            var project = _solution.ProjectContainingFile(request.FileName);
            if (project.CompilerSettings != null) {
            	parser.CompilerSettings = project.CompilerSettings;
            }

            var filename = request.FileName.ApplyPathReplacementsForClient();

            var razorUtilities = new RazorUtilities();
            CSharpConversionResult razorOutput = null;
            var buffer = request.Buffer;
            if (razorUtilities.IsRazor(request))
            {
                razorOutput = razorUtilities.ConvertToCSharp(request.FileName, buffer);
                //System.Console.WriteLine(" Pre:```````````````\n"+buffer+"\n''''''''''''\n");
                //System.Console.WriteLine("Post:```````````````\n"+razorOutput.Source+"\n''''''''''''\n");
                if (!razorOutput.Success)
                {
                    var razorErrors = razorOutput.Errors.Select(error => new Error
                    {
                        Message = error.Message.Replace("'", "''"),
                        Column = error.Column +1,
                        Line = error.Line + 1,
                        FileName = filename
                    });
                    return new SyntaxErrorsResponse {Errors = razorErrors};
                }
                buffer = razorOutput.Source;
            }

            var syntaxTree = parser.Parse(buffer, request.FileName);

            var errors = syntaxTree.Errors.Select(error => new Error
                {
                    Message = error.Message.Replace("'", "''"),
                    Column = error.Region.BeginColumn,
                    Line = error.Region.BeginLine,
                    EndColumn = error.Region.EndColumn,
                    EndLine = error.Region.EndLine,
                    FileName = filename
                });

            if (razorOutput != null)
            {
                errors = errors
                    .Select(error => new {
                        oldLocation = razorOutput.ConvertToOldLocation(error.Line, error.Column),
                        oldEndLocation = razorOutput.ConvertToOldLocation(error.EndLine, error.EndColumn),
                        error
                    })
                    .Where(i => i.oldLocation != null)
                    .Select(errorStruct =>
                        new Error {
                            Message = errorStruct.error.Message,
                            Column = errorStruct.oldLocation.Value.Column,
                            Line = errorStruct.oldLocation.Value.Line,
                            EndColumn = errorStruct.oldEndLocation.GetValueOrDefault(errorStruct.oldLocation.Value).Column,
                            EndLine = errorStruct.oldEndLocation.GetValueOrDefault(errorStruct.oldLocation.Value).Line,
                            FileName = errorStruct.error.FileName,
                        }
                    );
            }

            return new SyntaxErrorsResponse {Errors = errors};
        }
    }
}
