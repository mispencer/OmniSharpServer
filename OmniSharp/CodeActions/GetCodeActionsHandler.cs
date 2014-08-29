using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ICSharpCode.NRefactory;
using ICSharpCode.NRefactory.CSharp.Refactoring;
using OmniSharp.Common;
using OmniSharp.Configuration;
using OmniSharp.Parser;
using OmniSharp.Refactoring;
using OmniSharp.Razor;

namespace OmniSharp.CodeActions
{
    public class GetCodeActionsHandler
    {
        readonly BufferParser _bufferParser;
        readonly OmniSharpConfiguration _config;

        public GetCodeActionsHandler(BufferParser bufferParser, OmniSharpConfiguration config)
        {
            _bufferParser = bufferParser;
            _config = config;
        }

        public GetCodeActionsResponse GetCodeActions(Request req)
        {
            var actions = GetContextualCodeActions(req);

            return new GetCodeActionsResponse { CodeActions = actions.Select(a =>  a.Description) };
        }

        public RunCodeActionsResponse RunCodeAction(CodeActionRequest req)
        {
            var actions = GetContextualCodeActions(req).ToList();
            if(req.CodeAction > actions.Count)
                return new RunCodeActionsResponse();

            var context = OmniSharpRefactoringContext.GetContext(_bufferParser, req);
            
            using (var script = new OmniSharpScript(context, _config))
            {
				CodeAction action = actions[req.CodeAction];
                action.Run(script);
            }

            return new RunCodeActionsResponse {Text = context.Document.Text};
        }

        private IEnumerable<CodeAction> GetContextualCodeActions(Request request)
        {
            var razorUtilities = new RazorUtilities();
            CSharpConversionResult razorOutput = null;
            if (razorUtilities.IsRazor(request))
            {
                razorOutput = razorUtilities.ConvertToCSharp(request.FileName, request.Buffer);
                if (!razorOutput.Success)
                {
                    return new List<CodeAction>();
                }
                request.Buffer = razorOutput.Source;
            }

            var refactoringContext = OmniSharpRefactoringContext.GetContext(_bufferParser, request);

            var actions = new List<CodeAction>();
            var providers = new CodeActionProviders().GetProviders();
            foreach (var provider in providers)
            {
                var providerActions = provider.GetActions(refactoringContext);
                actions.AddRange(providerActions);
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
                        action.GetType().GetProperty("Start", BindingFlags.NonPublic).SetValue(action, new TextLocation(oldStart.Value.Line, oldStart.Value.Column), null);
                        action.GetType().GetProperty("End", BindingFlags.NonPublic).SetValue(action, new TextLocation(oldEnd.Value.Line, oldEnd.Value.Column), null);
                    }
                }
            }

            return actions;
        }
    }
}
