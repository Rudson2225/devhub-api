namespace DevHub.Domain;

public static class Permissions
{
    public const string ProjectRead = "project.read";
    public const string ProjectUpdate = "project.update";
    public const string ProjectDelete = "project.delete";

    public const string MembersManage = "members.manage";

    public const string TasksRead = "tasks.read";
    public const string TasksCreate = "tasks.create";
    public const string TasksUpdate = "tasks.update";
    public const string TasksDelete = "tasks.delete";

    public const string CommentsRead = "comments.read";
    public const string CommentsCreate = "comments.create";
    public const string CommentsUpdateOwn = "comments.update_own";
    public const string CommentsUpdateAny = "comments.update_any";
    public const string CommentsDeleteOwn = "comments.delete_own";
    public const string CommentsDeleteAny = "comments.delete_any";

    public const string DocumentsRead = "documents.read";
    public const string DocumentsManage = "documents.manage";

    public const string TagsRead = "tags.read";
    public const string TagsManage = "tags.manage";

    public const string ActivitiesRead = "activities.read";
    public const string DashboardRead = "dashboard.read";
}
