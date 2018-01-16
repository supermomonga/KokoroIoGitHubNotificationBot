using System;
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
using Shipwreck.GithubClient.Events;

namespace KokoroIoGitHubNotificationBot
{
    public sealed class IncomingWebhook
    {
        private class HttpException : Exception
        {
            public HttpException(HttpStatusCode statusCode, string statusDescription)
            {
                StatusCode = statusCode;
                StatusDescription = statusDescription;
            }

            public HttpStatusCode StatusCode { get; }
            public string StatusDescription { get; }
        }

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
            try
            {
                if (string.IsNullOrEmpty(AccessToken))
                {
                    throw new HttpException(HttpStatusCode.BadRequest, "Missing configration: AccessToken");
                }

                using (var tr = Secret != null
                                    ? await OpenReaderIfValidAsync(context).ConfigureAwait(false)
                                    : new StreamReader(context.Request.Body))
                using (var jr = new JsonTextReader(tr))
                {
                    HandleEventAsync(context, jr).GetHashCode();

                    WriteResponse(context, HttpStatusCode.OK, string.Empty);
                }
            }
            catch (HttpException hex)
            {
                WriteResponse(context, hex.StatusCode, hex.StatusDescription);
            }
        }

        #region Handle events

        private Task HandleEventAsync(HttpContext context, JsonTextReader jsonReader)
        {
            switch (GetEventType(context))
            {
                case EventTypes.Ping:
                    return HandlePingEventAsync(context, jsonReader);

                case EventTypes.Issues:
                    return HandleIssuesEventAsync(context, jsonReader);

                case EventTypes.Create:
                    return HandleCreateEventAsync(context, jsonReader);

                case EventTypes.Delete:
                    return HandleDeleteEventAsync(context, jsonReader);

                case EventTypes.Push:
                    return HandlePushEventAsync(context, jsonReader);

                case EventTypes.IssueComment:
                    return HandleIssueCommentEventAsync(context, jsonReader);

                case EventTypes.PullRequest:
                    return HandlePullRequestEventAsync(context, jsonReader);

                case EventTypes.PullRequestReviewComment:
                    return HandlePullRequestReviewCommentEventAsync(context, jsonReader);

                case EventTypes.PullRequestReview:
                    return HandlePullRequestReviewEventAsync(context, jsonReader);

                case EventTypes.Label:
                    return HandleLabelEventAsync(context, jsonReader);

                case EventTypes.Gollum:
                    return HandleGollumEventAsync(context, jsonReader);

                case EventTypes.Member:
                    return HandleMemberEventAsync(context, jsonReader);

                case EventTypes.Public:
                    return HandlePublicEventAsync(context, jsonReader);

                case EventTypes.Watch:
                    return HandleWatchEventAsync(context, jsonReader);

                case EventTypes.Project:
                    return HandleProjectEventAsync(context, jsonReader);

                case EventTypes.ProjectColumn:
                    return HandleProjectColumnEventAsync(context, jsonReader);

                case EventTypes.ProjectCard:
                    return HandleProjectCardEventAsync(context, jsonReader);

                case EventTypes.Status:
                    return HandleStatusEventAsync(context, jsonReader);

                case EventTypes.CommitComment:
                    return HandleCommitCommentEventAsync(context, jsonReader);

                case EventTypes.Deployment:
                    return HandleDeploymentEventAsync(context, jsonReader);

                case EventTypes.DeploymentStatus:
                    return HandleDeploymentStatusEventAsync(context, jsonReader);

                case EventTypes.Download:
                    return HandleDownloadEventAsync(context, jsonReader);

                case EventTypes.Follow:
                    return HandleFollowEventAsync(context, jsonReader);

                case EventTypes.Fork:
                    return HandleForkEventAsync(context, jsonReader);

                case EventTypes.ForkApply:
                    return HandleForkApplyEventAsync(context, jsonReader);

                case EventTypes.Gist:
                    return HandleGistEventAsync(context, jsonReader);

                case EventTypes.Installation:
                    return HandleInstallationEventAsync(context, jsonReader);

                case EventTypes.InstallationRepositories:
                    return HandleInstallationRepositoriesEventAsync(context, jsonReader);

                case EventTypes.MarketplacePurchase:
                    return HandleMarketplacePurchaseEventAsync(context, jsonReader);

                case EventTypes.Membership:
                    return HandleMembershipEventAsync(context, jsonReader);

                case EventTypes.Milestone:
                    return HandleMilestoneEventAsync(context, jsonReader);

                case EventTypes.Organization:
                    return HandleOrganizationEventAsync(context, jsonReader);

                case EventTypes.OrgBlock:
                    return HandleOrgBlockEventAsync(context, jsonReader);

                case EventTypes.PageBuild:
                    return HandlePageBuildEventAsync(context, jsonReader);

                case EventTypes.Release:
                    return HandleReleaseEventAsync(context, jsonReader);

                case EventTypes.Repository:
                    return HandleRepositoryEventAsync(context, jsonReader);

                case EventTypes.Team:
                    return HandleTeamEventAsync(context, jsonReader);

                case EventTypes.TeamAdd:
                    return HandleTeamAddEventAsync(context, jsonReader);

                default:
                    return HandleUnknownEventAsync(context, jsonReader);
            }
        }

        #region Dedicated implementations

        private async Task HandlePingEventAsync(HttpContext context, JsonReader jsonReader)
        {
            var data = DeserializeAs<PingEventPayload>(jsonReader);

            using (var sw = new StringWriter())
            {
                data.Repository.WriteLinkLineTo(sw);
                sw.WriteLine("__Ping received.__");
                sw.WriteBlockQoute(data.Zen);

                await PostMessageAsync(GetChannelId(context), sw.ToString()).ConfigureAwait(false);
            }
        }

        private async Task HandleIssuesEventAsync(HttpContext context, JsonReader jsonReader)
        {
            var data = DeserializeAs<IssuesEventPayload>(jsonReader);

            if (data.Action == IssueAction.Labeled)
            {
                return;
            }

            var issue = data.Issue;

            using (var sw = new StringWriter())
            {
                data.Repository.WriteLinkLineTo(sw);
                sw.Write($"__The issue ");

                issue.WriteLinkTo(sw);

                sw.Write($" ");
                sw.Write(data.Action.ToString().ToLowerInvariant());
                sw.Write($" by ");

                data.Sender.WriteLinkTo(sw);

                sw.WriteLine($".__");

                if (data.Action == IssueAction.Opened)
                {
                    sw.WriteBlockQoute(issue.Body);
                }

                await PostMessageAsync(GetChannelId(context), sw.ToString()).ConfigureAwait(false);
            }
        }

        private async Task HandleCreateEventAsync(HttpContext context, JsonReader jsonReader)
        {
            var data = DeserializeAs<CreateEventPayload>(jsonReader);

            using (var sw = new StringWriter())
            {
                data.Repository.WriteLinkLineTo(sw);

                sw.Write("__New ");
                sw.Write(data.RefType);
                sw.Write(" `");
                sw.Write(data.Ref);
                sw.Write("` created by ");

                data.Sender.WriteLinkTo(sw);

                sw.WriteLine($".__");

                sw.WriteBlockQoute(data.Description);

                await PostMessageAsync(GetChannelId(context), sw.ToString()).ConfigureAwait(false);
            }
        }

        private async Task HandleDeleteEventAsync(HttpContext context, JsonReader jsonReader)
        {
            var data = DeserializeAs<DeleteEventPayload>(jsonReader);

            using (var sw = new StringWriter())
            {
                data.Repository.WriteLinkLineTo(sw);

                sw.Write("__A ");
                sw.Write(data.RefType);
                sw.Write(" named `");
                sw.Write(data.Ref);
                sw.Write("` was deleted by ");
                data.Sender.WriteLinkTo(sw);
                sw.WriteLine(".__");

                await PostMessageAsync(GetChannelId(context), sw.ToString()).ConfigureAwait(false);
            }
        }

        private async Task HandlePushEventAsync(HttpContext context, JsonReader jsonReader)
        {
            var data = DeserializeAs<PushEventPayload>(jsonReader);

            if (!(data.Commits.Length > 0))
            {
                return;
            }

            using (var sw = new StringWriter())
            {
                data.Repository.WriteLinkLineTo(sw);

                sw.Write("__");
                sw.Write(data.Commits.Length);
                sw.Write(" commits pushed to branch [");
                sw.Write(data.Ref.Split('/').Last());
                sw.Write("](");
                sw.Write(data.Compare);
                sw.WriteLine(").__");

                foreach (var c in data.Commits)
                {
                    c.WriteLinkTo(sw);

                    sw.Write(" ");
                    sw.Write(c.Message);
                    sw.Write(" - ");
                    sw.WriteLine(c.Author.Username);
                }

                await PostMessageAsync(GetChannelId(context), sw.ToString()).ConfigureAwait(false);
            }
        }

        private async Task HandleIssueCommentEventAsync(HttpContext context, JsonReader jsonReader)
        {
            var data = DeserializeAs<IssueCommentEventPayload>(jsonReader);

            using (var sw = new StringWriter())
            {
                data.Repository.WriteLinkLineTo(sw);

                sw.Write("__New comment ");
                sw.Write(data.Action.ToString().ToLowerInvariant());
                sw.Write(" by ");

                data.Sender.WriteLinkTo(sw);

                sw.Write(" on issue ");

                data.Issue.WriteLinkTo(sw, data.Comment.HtmlUrl);

                sw.WriteLine(".__");

                sw.WriteBlockQoute(data.Comment.Body);

                await PostMessageAsync(GetChannelId(context), sw.ToString()).ConfigureAwait(false);
            }
        }

        private async Task HandlePullRequestReviewCommentEventAsync(HttpContext context, JsonReader jsonReader)
        {
            var data = DeserializeAs<PullRequestReviewCommentEventPayload>(jsonReader);

            if (data.Action != EditAction.Created)
            {
                return;
            }

            using (var sw = new StringWriter())
            {
                data.Repository.WriteLinkLineTo(sw);

                sw.Write("__New comment ");
                sw.Write(data.Action.ToString().ToLowerInvariant());
                sw.Write(" by ");

                data.Sender.WriteLinkTo(sw);

                sw.Write(" on pull request ");

                data.PullRequest.WriteLinkTo(sw, data.Comment.HtmlUrl);

                sw.WriteLine(".__");

                sw.WriteBlockQoute(data.Comment.Body);

                await PostMessageAsync(GetChannelId(context), sw.ToString()).ConfigureAwait(false);
            }
        }

        private async Task HandlePullRequestEventAsync(HttpContext context, JsonReader jsonReader)
        {
            var data = DeserializeAs<PullRequestEventPayload>(jsonReader);

            using (var sw = new StringWriter())
            {
                data.Repository.WriteLinkLineTo(sw);

                var pr = data.PullRequest;

                sw.Write("__The pull request ");

                pr.WriteLinkTo(sw);
                sw.Write(" ");

                if (data.Action == PullRequestAction.ReviewRequested)
                {
                    sw.Write("requested review");
                    var max = (pr.RequestedReviewers?.Length ?? 0) - 1;
                    if (max >= 0)
                    {
                        for (int i = 0; i <= max; i++)
                        {
                            var rr = pr.RequestedReviewers[i];
                            sw.Write(i == 0 ? " to " : i == max ? " and " : ", ");
                            rr.WriteLinkTo(sw);
                        }
                    }
                }
                else
                {
                    sw.Write(data.Action.ToString().ToLowerInvariant());
                }
                sw.Write(" by ");

                data.Sender.WriteLinkTo(sw);

                sw.WriteLine(".__");

                if (data.Action == PullRequestAction.Opened)
                {
                    sw.WriteBlockQoute(pr.Body);
                }

                await PostMessageAsync(GetChannelId(context), sw.ToString()).ConfigureAwait(false);
            }
        }

        #endregion Dedicated implementations

        #region No action

        private Task HandlePullRequestReviewEventAsync(HttpContext context, JsonReader jsonReader)
            => Task.FromResult(0);

        private Task HandleLabelEventAsync(HttpContext context, JsonReader jsonReader)
            => Task.FromResult(0);

        private Task HandleGollumEventAsync(HttpContext context, JsonReader jsonReader)
            => Task.FromResult(0);

        private Task HandleMemberEventAsync(HttpContext context, JsonReader jsonReader)
            => Task.FromResult(0);

        private Task HandleProjectCardEventAsync(HttpContext context, JsonReader jsonReader)
            => Task.FromResult(0);

        private Task HandleProjectColumnEventAsync(HttpContext context, JsonReader jsonReader)
            => Task.FromResult(0);

        private Task HandleProjectEventAsync(HttpContext context, JsonReader jsonReader)
            => Task.FromResult(0);

        private Task HandlePublicEventAsync(HttpContext context, JsonReader jsonReader)
            => Task.FromResult(0);

        private Task HandleStatusEventAsync(HttpContext context, JsonReader jsonReader)
            => Task.FromResult(0);

        #endregion No action

        #region Unknown events

        private async Task HandleUnknownEventAsync(HttpContext context, JsonReader jsonReader)
        {
            var data = DeserializeAs<ActivityPayload>(jsonReader);

            using (var sw = new StringWriter())
            {
                data.Repository.WriteLinkLineTo(sw);
                sw.Write("__Unsupported event: `");

                context.Request.Headers.TryGetValue("X-Github-Event", out var sv);
                sw.Write(sv.FirstOrDefault());

                sw.WriteLine("`.__");

                await PostMessageAsync(GetChannelId(context), sw.ToString()).ConfigureAwait(false);
            }
        }

        private Task HandleCommitCommentEventAsync(HttpContext context, JsonReader jsonReader)
            => HandleUnknownEventAsync(context, jsonReader);

        private Task HandleDeploymentEventAsync(HttpContext context, JsonReader jsonReader)
            => HandleUnknownEventAsync(context, jsonReader);

        private Task HandleDeploymentStatusEventAsync(HttpContext context, JsonReader jsonReader)
            => HandleUnknownEventAsync(context, jsonReader);

        private Task HandleDownloadEventAsync(HttpContext context, JsonReader jsonReader)
            => HandleUnknownEventAsync(context, jsonReader);

        private Task HandleFollowEventAsync(HttpContext context, JsonReader jsonReader)
            => HandleUnknownEventAsync(context, jsonReader);

        private Task HandleForkEventAsync(HttpContext context, JsonReader jsonReader)
            => HandleUnknownEventAsync(context, jsonReader);

        private Task HandleForkApplyEventAsync(HttpContext context, JsonReader jsonReader)
            => HandleUnknownEventAsync(context, jsonReader);

        private Task HandleGistEventAsync(HttpContext context, JsonReader jsonReader)
            => HandleUnknownEventAsync(context, jsonReader);

        private Task HandleInstallationEventAsync(HttpContext context, JsonReader jsonReader)
            => HandleUnknownEventAsync(context, jsonReader);

        private Task HandleInstallationRepositoriesEventAsync(HttpContext context, JsonReader jsonReader)
            => HandleUnknownEventAsync(context, jsonReader);

        private Task HandleMarketplacePurchaseEventAsync(HttpContext context, JsonReader jsonReader)
            => HandleUnknownEventAsync(context, jsonReader);

        private Task HandleMembershipEventAsync(HttpContext context, JsonReader jsonReader)
            => HandleUnknownEventAsync(context, jsonReader);

        private Task HandleMilestoneEventAsync(HttpContext context, JsonReader jsonReader)
            => HandleUnknownEventAsync(context, jsonReader);

        private Task HandleOrganizationEventAsync(HttpContext context, JsonReader jsonReader)
            => HandleUnknownEventAsync(context, jsonReader);

        private Task HandleOrgBlockEventAsync(HttpContext context, JsonReader jsonReader)
            => HandleUnknownEventAsync(context, jsonReader);

        private Task HandlePageBuildEventAsync(HttpContext context, JsonReader jsonReader)
            => HandleUnknownEventAsync(context, jsonReader);

        private Task HandleReleaseEventAsync(HttpContext context, JsonReader jsonReader)
            => HandleUnknownEventAsync(context, jsonReader);

        private Task HandleRepositoryEventAsync(HttpContext context, JsonReader jsonReader)
            => HandleUnknownEventAsync(context, jsonReader);

        private Task HandleTeamEventAsync(HttpContext context, JsonReader jsonReader)
            => HandleUnknownEventAsync(context, jsonReader);

        private Task HandleTeamAddEventAsync(HttpContext context, JsonReader jsonReader)
            => HandleUnknownEventAsync(context, jsonReader);

        private Task HandleWatchEventAsync(HttpContext context, JsonReader jsonReader)
            => HandleUnknownEventAsync(context, jsonReader);

        #endregion Unknown events

        #endregion Handle events

        #region Helper methods

        private static EventTypes GetEventType(HttpContext context)
        {
            context.Request.Headers.TryGetValue("X-Github-Event", out var sv);
            Enum.TryParse<EventTypes>(sv.FirstOrDefault()?.Replace("_", ""), true, out var eventType);
            return eventType;
        }

        private async Task<StreamReader> OpenReaderIfValidAsync(HttpContext context)
        {
            context.Request.Headers.TryGetValue("X-Hub-Signature", out var sigs);
            var signatureHeader = sigs.FirstOrDefault();

            if (string.IsNullOrEmpty(signatureHeader))
            {
                throw new HttpException(HttpStatusCode.BadRequest, "Missing HTTP Header: X-Hub-Signature");
            }
            if (!signatureHeader.StartsWith("sha1=", StringComparison.OrdinalIgnoreCase))
            {
                throw new HttpException(HttpStatusCode.BadRequest, "Unknown Hash Algorithm");
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
                throw new HttpException(HttpStatusCode.BadRequest, "Invalid X-Hub-Signature");
            }

            return new StreamReader(new MemoryStream(body));
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

        private static T DeserializeAs<T>(JsonReader jsonReader)
            => new JsonSerializer().Deserialize<T>(jsonReader);

        private static string GetChannelId(HttpContext context)
        {
            string channelId = null;
            if (context.Request.Query.TryGetValue("channel", out var sv))
            {
                channelId = sv.FirstOrDefault();
            }

            if (string.IsNullOrEmpty(channelId))
            {
                throw new HttpException(HttpStatusCode.BadRequest, "Missing parameter: channel");
            }

            return channelId;
        }

        private async Task PostMessageAsync(string channelId, string message)
        {
            using (var bot = new BotClient()
            {
                AccessToken = AccessToken
            })
            {
                await bot.PostMessageAsync(channelId, message, expandEmbedContents: false).ConfigureAwait(false);
            }
        }

        private static void WriteResponse(HttpContext context, HttpStatusCode statusCode, string message)
        {
            context.Response.StatusCode = (int)statusCode;
            if (!string.IsNullOrEmpty(message))
            {
                context.Response.ContentType = "text/plain; charset=utf-8";

                var b = new UTF8Encoding(false).GetBytes(message);
                context.Response.Body.Write(b, 0, b.Length);
            }
        }

        #endregion Helper methods
    }
}
