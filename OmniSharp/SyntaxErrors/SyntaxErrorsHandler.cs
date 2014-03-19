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
            if (razorUtilities.IsRazor(request))
            {
                razorOutput = razorUtilities.ConvertToCSharp(request.FileName, request.Buffer);
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
                request.Buffer = razorOutput.Source;
            }

            var syntaxTree = parser.Parse(request.Buffer, request.FileName);

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
                errors = errors.Select(error => {
                    var oldLocation = razorOutput.ConvertToOldLocation(error.Line, error.Column);
                    var oldEndLocation = razorOutput.ConvertToOldLocation(error.EndLine, error.EndColumn);
                    return new Error {
                        Message = error.Message,
                        Column = oldLocation != null ? oldLocation.Value.Column : error.Column,
                        Line = oldLocation != null ? oldLocation.Value.Line : error.Line,
                        EndColumn = oldEndLocation != null ? oldEndLocation.Value.Column : error.EndColumn,
                        EndLine = oldEndLocation != null ? oldEndLocation.Value.Line : error.EndLine,
                        FileName = error.FileName,
                    };
                });
            }

            return new SyntaxErrorsResponse {Errors = errors};
        }
    }
}
