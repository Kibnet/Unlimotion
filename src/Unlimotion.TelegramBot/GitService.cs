using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using Microsoft.Extensions.Configuration;
using Serilog;
using Telegram.Bot.Types;

namespace Unlimotion.TelegramBot
{
    public class GitService
    {
        private readonly GitSettings settings;
        private readonly CredentialsHandler credentialsProvider;

        public GitService(IConfiguration configuration)
        {
            settings = configuration.Get<GitSettings>("Git");
            credentialsProvider = (_url, _user, _cred) =>
                new UsernamePasswordCredentials
                {
                    Username = settings.UserName,
                    Password = settings.Password
                };
        }

        public void CloneOrUpdateRepo()
        {
            try
            {
                if (!Repository.IsValid(settings.RepositoryPath))
                {
                    Log.Information("Клонирование репозитория из {RemoteUrl} в {RepoPath}", settings.RemoteUrl, settings.RepositoryPath);

                    var cloneOptions = new CloneOptions
                    {
                        BranchName = settings.Branch,
                        FetchOptions =
                        {
                            CredentialsProvider = credentialsProvider
                        }
                    };

                    Repository.Clone(settings.RemoteUrl, settings.RepositoryPath, cloneOptions);
                }
                else
                {
                    PullLatestChanges();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка при клонировании или обновлении репозитория");
            }
        }

        public void PullLatestChanges()
        {
            try
            {
                using var repo = new Repository(settings.RepositoryPath);
                Commands.Checkout(repo, settings.Branch);
                var options = new PullOptions
                {
                    FetchOptions = new FetchOptions
                    {
                        CredentialsProvider = credentialsProvider
                    }
                };
                var signature = new Signature(new Identity(settings.CommitterName, settings.CommitterEmail), DateTimeOffset.Now);
                var result = Commands.Pull(repo, signature, options);
                Log.Information("Выполнен pull изменений: {Status}", result.Status);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка при выполнении pull из репозитория");
            }
        }

        public void CommitAndPushChanges(string message)
        {
            try
            {
                using var repo = new Repository(settings.RepositoryPath);
                Commands.Checkout(repo, settings.Branch);
                Commands.Stage(repo, "*");
                if (repo.RetrieveStatus().IsDirty)
                {
                    var signature = new Signature(new Identity(settings.CommitterName, settings.CommitterEmail), DateTimeOffset.Now);
                    var commit = repo.Commit(message, signature, signature);
                    Log.Information("Создан коммит: {CommitMessage}", message);

                    var options = new PushOptions
                    {
                        CredentialsProvider = credentialsProvider
                    };
                    repo.Network.Push(repo.Head, options);
                    Log.Information("Изменения отправлены в удалённый репозиторий");
                }
                else
                {
                    Log.Information("Нет изменений для коммита");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка при выполнении commit/push в репозиторий");
            }
        }
    }
}
