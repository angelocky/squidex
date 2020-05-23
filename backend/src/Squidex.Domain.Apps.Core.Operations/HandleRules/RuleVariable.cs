﻿// ==========================================================================
//  Squidex Headless CMS
// ==========================================================================
//  Copyright (c) Squidex UG (haftungsbeschraenkt)
//  All rights reserved. Licensed under the MIT license.
// ==========================================================================

using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Squidex.Domain.Apps.Core.Contents;
using Squidex.Infrastructure.Json.Objects;
using Squidex.Shared.Identity;
using Squidex.Shared.Users;

namespace Squidex.Domain.Apps.Core.HandleRules
{
    public static class RuleVariable
    {
        private static readonly Regex RegexPatternOld = new Regex(@"^(?<Type>[^_]*)_(?<Path>[^\s]*)", RegexOptions.Compiled);
        private static readonly Regex RegexPatternNew = new Regex(@"^\{(?<Type>[^_]*)_(?<Path>[^\s]*)\}", RegexOptions.Compiled);

        public static (object? Result, string[] Remaining) GetValue(object @event, string[] path)
        {
            object? current = @event;

            var i = 0;

            for (; i < path.Length; i++)
            {
                var segment = path[i];

                if (current is NamedContentData data)
                {
                    if (!data.TryGetValue(segment, out var temp) || temp == null)
                    {
                        return (null, path.Skip(i).ToArray());
                    }

                    current = temp;
                }
                else if (current is ContentFieldData field)
                {
                    if (!field.TryGetValue(segment, out var temp) || temp == null)
                    {
                        return (null, path.Skip(i).ToArray());
                    }

                    current = temp;
                }
                else if (current is IJsonValue json)
                {
                    if (!json.TryGet(segment, out var temp) || temp == null || temp.Type == JsonValueType.Null)
                    {
                        return (null, path.Skip(i).ToArray());
                    }

                    current = temp;
                }
                else if (current != null)
                {
                    if (current is IUser user)
                    {
                        var type = segment;

                        if (string.Equals(type, "Name", StringComparison.OrdinalIgnoreCase))
                        {
                            type = SquidexClaimTypes.DisplayName;
                        }

                        var claim = user.Claims.FirstOrDefault(x => string.Equals(x.Type, type, StringComparison.OrdinalIgnoreCase));

                        if (claim != null)
                        {
                            current = claim.Value;
                            continue;
                        }
                    }

                    const BindingFlags bindingFlags =
                        BindingFlags.FlattenHierarchy |
                        BindingFlags.Public |
                        BindingFlags.Instance;

                    var properties = current.GetType().GetProperties(bindingFlags);
                    var property = properties.FirstOrDefault(x => x.CanRead && string.Equals(x.Name, segment, StringComparison.OrdinalIgnoreCase));

                    if (property == null)
                    {
                        return (null, path.Skip(i).ToArray());
                    }

                    current = property.GetValue(current);
                }
                else
                {
                    return (null, path.Skip(i).ToArray());
                }
            }

            return (current, path.Skip(i).ToArray());
        }
    }
}
