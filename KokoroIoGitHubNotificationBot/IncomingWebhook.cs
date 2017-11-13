using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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
        private readonly string accessToken;

        public IncomingWebhook(IConfigurationRoot configuration)
        {
            Configuration = configuration;
            accessToken = Configuration["AccessToken"];
        }

        public async Task HandleAsync(HttpContext context)
        {
            using (var tr = new StreamReader(context.Request.Body))
            using (var jr = new JsonTextReader(tr))
            {
                dynamic data = await JObject.LoadAsync(jr).ConfigureAwait(false);

                string channelId = null;
                if (context.Request.Query.TryGetValue("channel", out var sv))
                {
                    channelId = sv.FirstOrDefault();
                }

                if (string.IsNullOrEmpty(accessToken))
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

                using (var bot = new BotClient() { AccessToken = accessToken })
                {
                    bot.EndPoint = "https://kokoro.io/api";
                    await bot.PostMessageAsync(channelId, message);
                }

                CreateResponse(context, HttpStatusCode.OK, message);
            }
        }

        private static void CreateResponse(HttpContext context, HttpStatusCode statusCode, string message)
        {
            context.Response.StatusCode = (int)statusCode;
        }
    }
}