using Newtonsoft.Json.Linq;

namespace Slackord
{
    internal class UserService
    {
        private Dictionary<string, User> users;

        private UserService(Dictionary<string, User> users)
        {
            this.users = users;
        }

        public static UserService fromFile(FileInfo usersFile)
        {
            Dictionary<string, User> users = readUsers(usersFile);
            return new UserService(users);
        }

        private static Dictionary<string, User> readUsers(FileInfo usersFile) {
            Dictionary<string, User> users = new();
            var json = File.ReadAllText(usersFile.FullName);
            var jarray = JArray.Parse(json);

            foreach (JObject jobject in jarray.Cast<JObject>())
            {
                var id = jobject["id"].ToString();
                var realName = jobject["profile"]["real_name"].ToString();
                var displayName = jobject["profile"]["display_name"]?.ToString();
                var user = new User(id, realName, displayName);
                users.Add(id, user);
            }

            return users;
        }

        public User userById(string id)
        {
            if (users == null)
            {
                return null;
            } else  {
                return users.GetValueOrDefault(id, null);
            }
        }
    }

    public class User
    {
        public User(string id, string realName, string displayName)
        {
            Id = id;
            RealName = realName;
            DisplayName = displayName;
        }

        public User(string id)
        {
            Id = id;
        }
        public string Id {init; get;}
        public string RealName {init; get;}
        public string DisplayName {init; get;}

        public string Render()
        {
            if (DisplayName != null)
            {
                return DisplayName;
            }
            else if (RealName != null)
            {
                return RealName;
            }
            else
            {
                return Id;
            }
        }
    }
}