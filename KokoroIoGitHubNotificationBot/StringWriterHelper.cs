using System.IO;
using Shipwreck.GithubClient;

namespace KokoroIoGitHubNotificationBot
{
    internal static class StringWriterHelper
    {
        public static void WriteLinkLineTo(this Repository repository, StringWriter writer)
        {
            writer.Write("__\\[[");
            writer.Write(repository.FullName);
            writer.Write("](");
            writer.Write(repository.HtmlUrl);
            writer.WriteLine(")\\]__");
        }

        public static void WriteLinkTo(this PullRequest pr, StringWriter sw, string url = null)
        {
            sw.Write("[#");
            sw.Write(pr.Number);
            sw.Write(": ");
            sw.Write(pr.Title);
            sw.Write("](");
            sw.Write(url ?? pr.HtmlUrl);
            sw.Write(")");
        }

        public static void WriteLinkTo(this Issue issue, StringWriter sw, string url = null)
        {
            sw.Write("[#");
            sw.Write(issue.Number);
            sw.Write(": ");
            sw.Write(issue.Title);
            sw.Write("](");
            sw.Write(url ?? issue.HtmlUrl);
            sw.Write(")");
        }

        public static void WriteLinkTo(this Account account, StringWriter sw)
        {
            sw.Write("[#");
            sw.Write(account.Login);
            sw.Write("](");
            sw.Write(account.HtmlUrl);
            sw.Write(")");
        }
        public static void WriteLinkTo(this Commit c, StringWriter sw)
        {
            sw.Write("[`");
            sw.WriteShortHash(c.Id);
            sw.Write("`](");
            sw.Write(c.Url);
            sw.Write(")");
        }

        public static void WriteBlockQoute(this StringWriter sw, string s)
        {
            using (var sr = new StringReader(s))
            {
                for (var l = sr.ReadLine(); l != null; l = sr.ReadLine())
                {
                    if (l.Length > 0)
                    {
                        sw.Write("> ");
                        sw.WriteLine(l);
                    }
                }
            }
        }

        public static void WriteShortHash(this StringWriter sw, string cid)
        {
            for (var i = 0; i < 7; i++)
            {
                sw.Write(cid[i]);
            }
        }
    }
}