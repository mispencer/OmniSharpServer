using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Reflection;
using ICSharpCode.NRefactory;
using ICSharpCode.NRefactory.CSharp.Refactoring;
using OmniSharp.Common;
using OmniSharp.Configuration;
using OmniSharp.Parser;
using OmniSharp.Razor;
using OmniSharp.Refactoring;

namespace OmniSharp.CodeIssues
{
    public class CodeIssuesHandler
    {
        private readonly BufferParser _bufferParser;
        private readonly OmniSharpConfiguration _config;
        private readonly IEnumerable<string> _ignoredCodeIssues;

        public CodeIssuesHandler(BufferParser bufferParser, OmniSharpConfiguration config)
        {
            _bufferParser = bufferParser;
            _config = config;
            _ignoredCodeIssues = ConfigurationLoader.Config.IgnoredCodeIssues;
        }

        public QuickFixResponse GetCodeIssues(Request req)
        {
            var actions = GetContextualCodeActions(req);
            return new QuickFixResponse(actions.Select(a => new QuickFix
                {
                    Column = a.Start.Column,
                    Line = a.Start.Line,
                    EndColumn = a.End.Column,
                    EndLine = a.End.Line,
                    FileName = req.FileName,
                    Text = a.Description,
                    LogLevel = "Warning"
                }));
        }

        public RunCodeIssuesResponse FixCodeIssue(Request req)
        {
            var issues = GetContextualCodeActions(req).ToList();

            var issue = issues.FirstOrDefault(i => i.Start.Line == req.Line);
            if (issue == null)
                return new RunCodeIssuesResponse { Text = req.Buffer };

            var context = OmniSharpRefactoringContext.GetContext(_bufferParser, req);
            
            using (var script = new OmniSharpScript(context, _config))
            {
                var action = issue.Actions.FirstOrDefault();
                if (action != null)
                {
                    action.Run(script);
                    return new RunCodeIssuesResponse {Text = context.Document.Text};
                }
            }

            return new RunCodeIssuesResponse {Text = req.Buffer};
        }

        private IEnumerable<CodeIssue> GetContextualCodeActions(Request req)
        {
            var razorUtilities = new RazorUtilities();
            CSharpConversionResult razorOutput = null;
            if (razorUtilities.IsRazor(req))
            {
                razorOutput = razorUtilities.ConvertToCSharp(req.FileName, req.Buffer);
                if (!razorOutput.Success)
                {
                    return new List<CodeIssue>();
                }
                req.Buffer = razorOutput.Source;
            }

            var refactoringContext = OmniSharpRefactoringContext.GetContext(_bufferParser, req);
            var actions = new List<CodeIssue>();
            var providers = new CodeIssueProviders().GetProviders();
            foreach (var provider in providers)
            {
                try
                {
                    var codeIssues = provider.GetIssues(refactoringContext);
                    actions.AddRange(codeIssues.Where(ShouldIncludeIssue));
                } 
                catch (Exception)
                {
                }
                
            }

            if (razorOutput != null)
            {
                foreach(var action in actions.ToList())
                {
                    var oldStart = razorOutput.ConvertToOldLocation(action.Start.Line, action.Start.Column);
                    var oldEnd = razorOutput.ConvertToOldLocation(action.End.Line, action.End.Column);
                    if (oldStart == null || oldEnd == null)
                    {
                        actions.Remove(action);
                    }
                    else
                    {
                        var startProp = action.GetType().GetProperty("Start", BindingFlags.Public|BindingFlags.Instance);
                        if (startProp != null) {
                            startProp.SetValue(action, new TextLocation(oldStart.Value.Line, oldStart.Value.Column), null);
                        }
                        else
                        {
                            Console.WriteLine("Couldn't find start: "+action.GetType().FullName);
                        }
                        var endProp = action.GetType().GetProperty("End", BindingFlags.Public|BindingFlags.Instance);
                        if (endProp != null) {
                            endProp.SetValue(action, new TextLocation(oldEnd.Value.Line, oldEnd.Value.Column), null);
                        }
                        else
                        {
                            Console.WriteLine("Couldn't find end: "+action.GetType().FullName);
                        }
                    }
                }
            }

            return actions;
        }

        private bool ShouldIncludeIssue(CodeIssue issue)
        {
            return !_ignoredCodeIssues.Any(ignore => Regex.IsMatch(issue.Description, ignore));
        }
    }
}
