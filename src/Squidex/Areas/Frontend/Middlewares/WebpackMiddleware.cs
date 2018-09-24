﻿// ==========================================================================
//  Squidex Headless CMS
// ==========================================================================
//  Copyright (c) Squidex UG (haftungsbeschränkt)
//  All rights reserved. Licensed under the MIT license.
// ==========================================================================

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Squidex.Areas.Frontend.Middlewares
{
    public sealed class WebpackMiddleware
    {
        private const string Host = "localhost";
        private const string Port = "3000";
        private static readonly string[] Scripts = { "shims.js", "app.js" };
        private static readonly string[] Styles = new string[0];
        private readonly RequestDelegate next;

        public WebpackMiddleware(RequestDelegate next)
        {
            this.next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            var responseBuffer = new MemoryStream();
            var responseBody = context.Response.Body;

            context.Response.Body = responseBuffer;

            await next(context);

            responseBuffer.Seek(0, SeekOrigin.Begin);

            if (context.Response.StatusCode == 200 && IsIndex(context) && IsHtml(context))
            {
                using (var reader = new StreamReader(responseBuffer))
                {
                    var response = await reader.ReadToEndAsync();

                    response = InjectStyles(response);
                    response = InjectScripts(response);

                    using (var memoryStream = new MemoryStream())
                    {
                        using (var writer = new StreamWriter(memoryStream))
                        {
                            writer.Write(response);
                            writer.Flush();

                            memoryStream.Seek(0, SeekOrigin.Begin);

                            context.Response.Headers["Content-Length"] = memoryStream.Length.ToString();

                            await memoryStream.CopyToAsync(responseBody);
                        }
                    }
                }
            }
            else if (context.Response.StatusCode != 304)
            {
                await responseBuffer.CopyToAsync(responseBody);
            }

            context.Response.Body = responseBody;
        }

        private static string InjectStyles(string response)
        {
            if (!response.Contains("</head>"))
            {
                return response;
            }

            var stylesTag = string.Empty;

            foreach (var file in Styles)
            {
                stylesTag += $"<link href=\"http://{Host}:{Port}/{file}\" rel=\"stylesheet\">";
            }

            response = response.Replace("</head>", $"{stylesTag}</head>");

            return response;
        }

        private static string InjectScripts(string response)
        {
            if (!response.Contains("</body>"))
            {
                return response;
            }

            var scriptsTag = string.Empty;

            foreach (var file in Scripts)
            {
                scriptsTag += $"<script type=\"text/javascript\" src=\"http://{Host}:{Port}/{file}\"></script>";
            }

            response = response.Replace("</body>", $"{scriptsTag}</body>");

            return response;
        }

        private static bool IsIndex(HttpContext context)
        {
            return context.Request.Path.Value.Equals("/index.html", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHtml(HttpContext context)
        {
            return context.Response.ContentType?.ToLower().Contains("text/html") == true;
        }
    }
}
