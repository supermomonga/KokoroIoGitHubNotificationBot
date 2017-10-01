using System;
using System.Linq;
using System.Net;
using System.Configuration;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using KokoroIO;
using System.Collections.Generic;

namespace KokoroIoGitHubNotificationBot
{
    public enum EventTypes
    {
        Unknown = 0, CommitComment, Create, Delete, Deployment, DeploymentStatus, Download, Follow, Fork, ForkApply, Gist, Gollum, Installation, InstallationRepositories, IssueComment, Issues, Label, MarketplacePurchase, Member, Membership, Milestone, Organization, OrgBlock, PageBuild, ProjectCard, ProjectColumn, Project, Public, PullRequest, PullRequestReview, PullRequestReviewComment, Push, Release, Repository, Status, Team, TeamAdd, Watch
    }
    public static class IncomingWebhook
    {
        [FunctionName("IncomingWebhook")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(WebHookType = "github")]HttpRequestMessage req, TraceWriter log)
        {
            // Get request body
            dynamic data = await req.Content.ReadAsAsync<object>();

            var channelId = req.GetQueryNameValuePairs().FirstOrDefault(kv => kv.Key == "channel").Value;

            var accessToken = ConfigurationManager.AppSettings.Get("AccessToken");

            if (string.IsNullOrEmpty(accessToken))
                return req.CreateResponse(HttpStatusCode.BadRequest, "Missing configration: AccessToken");

            if (string.IsNullOrEmpty(channelId))
                return req.CreateResponse(HttpStatusCode.BadRequest, "Missing parameter: channel");

            var eventType = GetEventType(req);
            var repositoryMeta = $"\\[[{ data.repository.full_name }]({ data.repository.html_url })\\]";
            var eventDescription = $"Unsupported event: `{ eventType }`";
            var eventMessage = "";

            // https://developer.github.com/v3/activity/events/types/
            switch(eventType)
            {
                case EventTypes.Issues:
                    var issue = data.issue;
                    var action = data.action;
                    if (action == "labeled")
                    {
                        return req.CreateErrorResponse(HttpStatusCode.OK, "OK");
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
                        return req.CreateErrorResponse(HttpStatusCode.OK, "OK");
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
                        return req.CreateErrorResponse(HttpStatusCode.OK, "OK");
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
                    return req.CreateErrorResponse(HttpStatusCode.OK, "OK");
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
                await bot.PostMessageAsync(channelId, message);
            }

            return req.CreateResponse(HttpStatusCode.OK, message);
        }

        private static EventTypes GetEventType(HttpRequestMessage req)
        {
            if(!req.Headers.TryGetValues("X-GitHub-Event", out IEnumerable<string> values))
                return EventTypes.Unknown;

            var eventName = values.FirstOrDefault()?.Replace("_", "");

            EventTypes.TryParse(eventName, true, out EventTypes eventType);
            return eventType;
        }
    }
}
