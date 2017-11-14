using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using KokoroIO;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KokoroIoGitHubNotificationBot
{
    public class IncomingWebhook
    {
        private readonly IConfigurationRoot Configuration;
        private readonly string AccessToken;
        private readonly byte[] Secret;

        public IncomingWebhook(IConfigurationRoot configuration)
        {
            Configuration = configuration;
            AccessToken = Configuration["AccessToken"];
            var secret = Configuration["WebhookSecret"];
            Secret = string.IsNullOrEmpty(secret) ? null : Encoding.ASCII.GetBytes(secret);
        }

        public async Task HandleAsync(HttpContext context)
        {
            StreamReader tr;
            if (Secret != null)
            {
                context.Request.Headers.TryGetValue("X-Hub-Signature", out var sigs);
                var signatureHeader = sigs.FirstOrDefault();

                if (string.IsNullOrEmpty(signatureHeader))
                {
                    CreateResponse(context, HttpStatusCode.BadRequest, "Missing HTTP Header: X-Hub-Signature");
                    return;
                }
                if (!signatureHeader.StartsWith("sha1=", StringComparison.OrdinalIgnoreCase))
                {
                    CreateResponse(context, HttpStatusCode.BadRequest, "Unknown Hash Algorithm");
                    return;
                }

                var signature = Enumerable.Range(0, (signatureHeader.Length - 5) / 2)
                                            .Select(i => (byte)(HexToInt(signatureHeader[5 + 2 * i]) * 16
                                                            + HexToInt(signatureHeader[6 + 2 * i])));

                byte[] body;
                using (var ms = new MemoryStream((int)(context.Request.ContentLength ?? 1024)))
                {
                    await context.Request.Body.CopyToAsync(ms).ConfigureAwait(false);
                    body = ms.ToArray();
                }
                var hmac = new HMACSHA1(Secret);

                var computed = hmac.ComputeHash(body);

                if (!computed.SequenceEqual(signature))
                {
                    CreateResponse(context, HttpStatusCode.BadRequest, "Invalid X-Hub-Signature");
                    return;
                }

                tr = new StreamReader(new MemoryStream(body));
            }
            else
            {
                tr = new StreamReader(context.Request.Body);
            }

            using (tr)
            using (var jr = new JsonTextReader(tr))
            {
                dynamic data = await JObject.LoadAsync(jr).ConfigureAwait(false);

                string channelId = null;
                if (context.Request.Query.TryGetValue("channel", out var sv))
                {
                    channelId = sv.FirstOrDefault();
                }

                if (string.IsNullOrEmpty(AccessToken))
                {
                    CreateResponse(context, HttpStatusCode.BadRequest, "Missing configration: AccessToken");
                    return;
                }

                if (string.IsNullOrEmpty(channelId))
                {
                    CreateResponse(context, HttpStatusCode.BadRequest, "Missing parameter: channel");
                    return;
                }

                context.Request.Headers.TryGetValue("X-Github-Event", out sv);
                Enum.TryParse<EventTypes>(sv.FirstOrDefault()?.Replace("_", ""), true, out var eventType);

                var repositoryMeta = $"\\[[{ data.repository.full_name }]({ data.repository.html_url })\\]";
                var eventDescription = $"Unsupported event: `{ eventType }`";
                var eventMessage = "";

                // https://developer.github.com/v3/activity/events/types/
                switch (eventType)
                {
                    case EventTypes.Ping:
                        eventDescription = $"Ping received.";
                        eventMessage = data.zen;
                        break;

                    case EventTypes.Issues:
                        var issue = data.issue;
                        var action = data.action;
                        if (action == "labeled")
                        {
                            CreateResponse(context, HttpStatusCode.OK, "OK");
                            return;
                        }
                        eventDescription = $"The issue [#{ issue.number }: { issue.title }]({ issue.html_url }) { data.action } by [{ issue.user.login }]({ issue.user.html_url })";
                        if (data.action == "opened")
                        {
                            eventMessage = issue.body;
                        }
                        break;

                    case EventTypes.Create:
                        eventDescription = $"New { data.ref_type } `{ data["ref"] }` created";
                        eventMessage = data.description;
                        break;

                    case EventTypes.Delete:
                        eventDescription = $"A { data.ref_type } named `{ data["ref"] }` was deleted by [{ data.sender.login }]({ data.sender.html_url })";
                        eventMessage = data.description;
                        break;

                    case EventTypes.Push:
                        IEnumerable<dynamic> commits = data.commits;
                        if (commits.Count() == 0)
                        {
                            CreateResponse(context, HttpStatusCode.OK, "OK");
                            return;
                        }
                        eventDescription = $"{ commits.Count() } commits pushed to branch [{ ((string)data["ref"]).Split('/').Last() }]({ data.compare })";
                        eventMessage = string.Join("\n", commits.Select(c => $"[`{ ((string)c.id).Substring(0, 7) }`]({ c.url }) { c.message } - { c.author.username }"));
                        break;

                    case EventTypes.IssueComment:
                        eventDescription = $"New comment { data.action } by [{ data.comment.user.login }]({ data.comment.user.html_url }) on issue [#{ data.issue.number }: { data.issue.title }]({ data.comment.html_url })";
                        eventMessage = data.comment.body;
                        break;

                    case EventTypes.PullRequest:
                        var pr = data.pull_request;
                        eventDescription = $"The pull request [#{ pr.number }: { pr.title }]({ pr.html_url }) { data.action } by [{ data.sender.login }]({ data.sender.html_url })";
                        if (data.action == "opened")
                        {
                            eventMessage = pr.body;
                        }
                        break;

                    case EventTypes.PullRequestReviewComment:
                        if (data.action == "created")
                        {
                            var c = data.comment;
                            eventDescription = $"[New comment]({ data.html_url }) posted to [{ data.pull_request.title }]({ data.pull_request.html_url }) by [{ c.user.login }]({ c.user.html_url })";
                            eventMessage = data.body;
                        }
                        else
                        {
                            CreateResponse(context, HttpStatusCode.OK, "OK");
                            return;
                        }
                        break;

                    case EventTypes.PullRequestReview:
                    case EventTypes.Label:
                    case EventTypes.Gollum:
                    case EventTypes.Member:
                    case EventTypes.Public:
                    case EventTypes.Watch:
                    case EventTypes.Project:
                    case EventTypes.ProjectColumn:
                    case EventTypes.ProjectCard:
                    case EventTypes.Status:
                        CreateResponse(context, HttpStatusCode.OK, "OK");
                        return;

                    default:
                        break;
                }
                var message = $"__{ repositoryMeta }__{ Environment.NewLine }__{ eventDescription }__";
                if (!string.IsNullOrEmpty(eventMessage))
                {
                    message += Environment.NewLine + string.Join(
                        Environment.NewLine,
                        eventMessage.Split(new string[] { Environment.NewLine }, StringSplitOptions.None).Select(l => $"> {l}")
                        );
                }

                using (var bot = new BotClient()
                {
                    AccessToken = AccessToken
                })
                {
                    await bot.PostMessageAsync(channelId, message).ConfigureAwait(false);
                }

                CreateResponse(context, HttpStatusCode.OK, message);
            }
        }

        private static void CreateResponse(HttpContext context, HttpStatusCode statusCode, string message)
        {
            context.Response.StatusCode = (int)statusCode;
            if (!string.IsNullOrEmpty(message))
            {
                context.Response.ContentType = "text/plain; charset=utf-8";

                var b = new UTF8Encoding(false).GetBytes(message);
                context.Response.Body.Write(b, 0, b.Length);
            }
        }

        private static int HexToInt(char c)
        {
            if ('0' <= c && c <= '9')
            {
                return c - '0';
            }
            if ('A' <= c && c <= 'F')
            {
                return c - 'A' + 10;
            }
            if ('a' <= c && c <= 'f')
            {
                return c - 'a' + 10;
            }
            throw new ArgumentOutOfRangeException();
        }
    }
}