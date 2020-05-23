// ==========================================================================
//  Squidex Headless CMS
// ==========================================================================
//  Copyright (c) Squidex UG (haftungsbeschraenkt)
//  All rights reserved. Licensed under the MIT license.
// =========================================-=================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Squidex.Domain.Apps.Core.Rules.EnrichedEvents;
using Squidex.Domain.Apps.Core.Scripting;
using Squidex.Infrastructure;
using Squidex.Infrastructure.Json;
using Squidex.Shared.Users;

namespace Squidex.Domain.Apps.Core.HandleRules
{
    public class RuleEventFormatter
    {
        private const string Fallback = "null";
        private static readonly Regex RegexPatternOld = new Regex(@"^(?<Type>[^_]*)_(?<Path>[^\s]*)", RegexOptions.Compiled);
        private static readonly Regex RegexPatternNew = new Regex(@"^\{(?<Type>[^_]*)_(?<Path>[^\s]*)\}", RegexOptions.Compiled);
        private readonly List<(char[] Pattern, Func<EnrichedEvent, string?> Replacer)> patterns = new List<(char[] Pattern, Func<EnrichedEvent, string?> Replacer)>();
        private readonly IJsonSerializer jsonSerializer;
        private readonly IEnumerable<IRuleEventFormatter> formatters;
        private readonly IUrlGenerator urlGenerator;
        private readonly IScriptEngine scriptEngine;

        public RuleEventFormatter(IJsonSerializer jsonSerializer, IEnumerable<IRuleEventFormatter> formatters, IScriptEngine scriptEngine)
        {
            Guard.NotNull(jsonSerializer, nameof(jsonSerializer));
            Guard.NotNull(scriptEngine, nameof(scriptEngine));
            Guard.NotNull(urlGenerator, nameof(urlGenerator));

            this.jsonSerializer = jsonSerializer;
            this.formatters = formatters;
            this.scriptEngine = scriptEngine;
            this.urlGenerator = urlGenerator;

            AddPattern("APP_ID", AppId);
            AddPattern("APP_NAME", AppName);
            AddPattern("ASSET_CONTENT_URL", AssetContentUrl);
            AddPattern("CONTENT_ACTION", ContentAction);
            AddPattern("CONTENT_URL", ContentUrl);
            AddPattern("MENTIONED_ID", MentionedId);
            AddPattern("MENTIONED_NAME", MentionedName);
            AddPattern("MENTIONED_EMAIL", MentionedEmail);
            AddPattern("SCHEMA_ID", SchemaId);
            AddPattern("SCHEMA_NAME", SchemaName);
            AddPattern("TIMESTAMP_DATETIME", TimestampTime);
            AddPattern("TIMESTAMP_DATE", TimestampDate);
            AddPattern("USER_ID", UserId);
            AddPattern("USER_NAME", UserName);
            AddPattern("USER_EMAIL", UserEmail);
        }

        private void AddPattern(string placeholder, Func<EnrichedEvent, string?> generator)
        {
            patterns.Add((placeholder.ToCharArray(), generator));
        }

        public virtual string ToPayload<T>(T @event)
        {
            return jsonSerializer.Serialize(@event);
        }

        public virtual string ToEnvelope(EnrichedEvent @event)
        {
            return jsonSerializer.Serialize(new { type = @event.Name, payload = @event, timestamp = @event.Timestamp });
        }

        public async ValueTask<string?> FormatAsync(string text, EnrichedEvent @event)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            if (TryGetScript(text.Trim(), out var script))
            {
                var context = new ScriptContext
                {
                    ["event"] = @event
                };

                return scriptEngine.Interpolate(context, script);
            }

            var parts = BuildParts(text, @event);

            await Task.WhenAll(parts.Select(x => x.Task.AsTask()));

            return BuildText(text, parts);
        }

        private static string BuildText(string text, List<(int Offset, int Length, ValueTask<string?> Task)> parts)
        {
            var span = text.AsSpan();

            var sb = new StringBuilder();

            foreach (var (offset, length, task) in parts)
            {
                if (task.Result != null)
                {
                    sb.Append(task.Result);
                }
                else
                {
                    sb.Append(span.Slice(offset, length));
                }
            }

            return sb.ToString();
        }

        private List<(int Offset, int Length, ValueTask<string?> Task)> BuildParts(string text, EnrichedEvent @event)
        {
            var parts = new List<(int Offset, int Length, ValueTask<string?> Task)>();

            var span = text.AsSpan();

            var currentOffset = 0;

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];

                if (c == '$')
                {
                    parts.Add((currentOffset, i - currentOffset, default));

                    var (replacement, length) = GetReplacement(span.Slice(i + 1), @event);

                    if (length > 0)
                    {
                        parts.Add((0, 0, replacement)!);

                        i += length + 1;
                    }

                    currentOffset = i;
                }
            }

            parts.Add((currentOffset, text.Length - currentOffset, default));

            return parts;
        }

        private (ValueTask<string> Result, int Length) GetReplacement(ReadOnlySpan<char> test, EnrichedEvent @event)
        {
            for (var j = 0; j < patterns.Count; j++)
            {
                var (pattern, replacer) = patterns[j];

                if (test.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    var result = replacer(@event) ?? Fallback;

                    return (new ValueTask<string>(result), pattern.Length);
                }
            }

            foreach (var formatter in formatters)
            {
                var (replaced, result, replacedLength) = formatter.Format(@event, test);

                if (replaced)
                {
                    return (new ValueTask<string>(result), replacedLength);
                }
            }

            var currentString = test.ToString();

            var match = RegexPatternNew.Match(currentString);

            if (!match.Success)
            {
                match = RegexPatternOld.Match(currentString);
            }

            if (match.Success)
            {
                var path = match.Groups["Path"].Value.Split('.', StringSplitOptions.RemoveEmptyEntries).ToArray();

                foreach (var formatter in formatters)
                {
                    var (replaced, result) = formatter.Format(@event, path);

                    if (replaced)
                    {
                        return (result, match.Length);
                    }
                }

                var (fallback, _) = RuleVariable.GetValue(@event, path);

                return (new ValueTask<string>(fallback?.ToString() ?? Fallback), match.Length);
            }

            return (new ValueTask<string>(Fallback), 0);
        }

        private static string TimestampDate(EnrichedEvent @event)
        {
            return @event.Timestamp.ToDateTimeUtc().ToString("yyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private static string TimestampTime(EnrichedEvent @event)
        {
            return @event.Timestamp.ToDateTimeUtc().ToString("yyy-MM-dd-hh-mm-ss", CultureInfo.InvariantCulture);
        }

        private static string AppId(EnrichedEvent @event)
        {
            return @event.AppId.Id.ToString();
        }

        private static string AppName(EnrichedEvent @event)
        {
            return @event.AppId.Name;
        }

        private static string? SchemaId(EnrichedEvent @event)
        {
            if (@event is EnrichedSchemaEventBase schemaEvent)
            {
                return schemaEvent.SchemaId.Id.ToString();
            }

            return null;
        }

        private static string? SchemaName(EnrichedEvent @event)
        {
            if (@event is EnrichedSchemaEventBase schemaEvent)
            {
                return schemaEvent.SchemaId.Name;
            }

            return null;
        }

        private static string? ContentAction(EnrichedEvent @event)
        {
            if (@event is EnrichedContentEvent contentEvent)
            {
                return contentEvent.Type.ToString();
            }

            return null;
        }

        private string? AssetContentUrl(EnrichedEvent @event)
        {
            if (@event is EnrichedAssetEvent assetEvent)
            {
                return urlGenerator.AssetContent(assetEvent.Id);
            }

            return null;
        }

        private string? ContentUrl(EnrichedEvent @event)
        {
            if (@event is EnrichedContentEvent contentEvent)
            {
                return urlGenerator.ContentUI(contentEvent.AppId, contentEvent.SchemaId, contentEvent.Id);
            }

            return null;
        }

        private static string? UserName(EnrichedEvent @event)
        {
            if (@event is EnrichedUserEventBase userEvent)
            {
                return userEvent.User?.DisplayName();
            }

            return null;
        }

        private static string? UserId(EnrichedEvent @event)
        {
            if (@event is EnrichedUserEventBase userEvent)
            {
                return userEvent.User?.Id;
            }

            return null;
        }

        private static string? UserEmail(EnrichedEvent @event)
        {
            if (@event is EnrichedUserEventBase userEvent)
            {
                return userEvent.User?.Email;
            }

            return null;
        }

        private static string? MentionedName(EnrichedEvent @event)
        {
            if (@event is EnrichedCommentEvent commentEvent)
            {
                return commentEvent.MentionedUser.DisplayName();
            }

            return null;
        }

        private static string? MentionedId(EnrichedEvent @event)
        {
            if (@event is EnrichedCommentEvent commentEvent)
            {
                return commentEvent.MentionedUser.Id;
            }

            return null;
        }

        private static string? MentionedEmail(EnrichedEvent @event)
        {
            if (@event is EnrichedCommentEvent commentEvent)
            {
                return commentEvent.MentionedUser.Email;
            }

            return null;
        }

        private static bool TryGetScript(string text, out string script)
        {
            const string ScriptSuffix = ")";
            const string ScriptPrefix = "Script(";

            script = null!;

            var comparer = StringComparison.OrdinalIgnoreCase;

            if (text.StartsWith(ScriptPrefix, comparer) && text.EndsWith(ScriptSuffix, comparer))
            {
                script = text.Substring(ScriptPrefix.Length, text.Length - ScriptPrefix.Length - ScriptSuffix.Length);
                return true;
            }

            return false;
        }
    }
}
