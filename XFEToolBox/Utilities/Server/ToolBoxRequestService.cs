using System.Text.Json;
using XFEToolBox.Client.Models.Server;
using XFEToolBox.Core.Downloads;
using XFEToolBox.Core.Models.Users;
using XFEToolBox.Core.Tools;
using XFEExtension.NetCore.ServerInteractive.Attributes;
using XFEExtension.NetCore.ServerInteractive.Implements.Requester;

namespace XFEToolBox.Client.Utilities.Server;

public partial class ToolBoxRequestService : StandardRequestServiceBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Request("v1/user/me", Name = "currentUser")]
    public object BuildCurrentUserRequest() => AuthenticatedBody("v1/user/me");

    [Response("v1/user/me", Name = "currentUser")]
    public object ParseCurrentUserResponse() => Deserialize<ToolBoxUserFaceInfo>();

    [Request("v1/user/register", Name = "register")]
    public object BuildRegisterRequest() => new
    {
        execute = "v1/user/register",
        userName = Parameters[0],
        password = Parameters[1],
        nickName = Parameters[2]
    };

    [Response("v1/user/register", Name = "register")]
    public object ParseRegisterResponse() => Deserialize<ToolBoxUserFaceInfo>();

    [Request("v1/user/profile/update", Name = "updateProfile")]
    public object BuildUpdateProfileRequest() => new
    {
        execute = "v1/user/profile/update",
        session = Session,
        deviceInfo = DeviceInfo,
        nickName = Parameters[0],
        bio = Parameters[1]
    };

    [Response("v1/user/profile/update", Name = "updateProfile")]
    public object ParseUpdateProfileResponse() => Deserialize<ToolBoxUserFaceInfo>();

    [Request("v1/user/password/change", Name = "changePassword")]
    public object BuildChangePasswordRequest() => new
    {
        execute = "v1/user/password/change",
        session = Session,
        deviceInfo = DeviceInfo,
        currentPassword = Parameters[0],
        newPassword = Parameters[1]
    };

    [Response("v1/user/password/change", Name = "changePassword")]
    public object ParseChangePasswordResponse() => Deserialize<JsonElement>();

    [Request("v1/tools/list", Name = "catalogTools")]
    public object BuildCatalogToolsRequest() => new
    {
        execute = "v1/tools/list",
        search = Parameters.Length > 0 ? Parameters[0] : null,
        category = Parameters.Length > 1 ? Parameters[1] : null
    };

    [Response("v1/tools/list", Name = "catalogTools")]
    public object ParseCatalogToolsResponse() => Deserialize<ToolPackageSummary[]>();

    [Request("v1/tools/get", Name = "catalogToolDetails")]
    public object BuildCatalogToolDetailsRequest() => new
    {
        execute = "v1/tools/get",
        toolId = Parameters[0]
    };

    [Response("v1/tools/get", Name = "catalogToolDetails")]
    public object ParseCatalogToolDetailsResponse() => Deserialize<ToolPackageDetails>();

    [Request("v1/software/list", Name = "softwareCatalog")]
    public object BuildSoftwareCatalogRequest() => new
    {
        execute = "v1/software/list",
        search = Parameters.Length > 0 ? Parameters[0] : null,
        category = Parameters.Length > 1 ? Parameters[1] : null
    };

    [Response("v1/software/list", Name = "softwareCatalog")]
    public object ParseSoftwareCatalogResponse() => Deserialize<SoftwareCatalogResponse>();

    [Request("v1/manage/overview", Name = "adminOverview")]
    public object BuildAdminOverviewRequest() => AuthenticatedBody("v1/manage/overview");

    [Response("v1/manage/overview", Name = "adminOverview")]
    public object ParseAdminOverviewResponse() => Deserialize<AdminOverview>();

    [Request("v1/manage/users/list", Name = "adminUsers")]
    public object BuildAdminUsersRequest() => AuthenticatedBody("v1/manage/users/list");

    [Response("v1/manage/users/list", Name = "adminUsers")]
    public object ParseAdminUsersResponse() => Deserialize<AdminUserItem[]>();

    [Request("v1/manage/users/create", Name = "adminCreateUser")]
    public object BuildCreateUserRequest() => new
    {
        execute = "v1/manage/users/create",
        session = Session,
        deviceInfo = DeviceInfo,
        userName = Parameters[0],
        password = Parameters[1],
        nickName = Parameters[2],
        isAdministrator = Parameters[3]
    };

    [Response("v1/manage/users/create", Name = "adminCreateUser")]
    public object ParseCreateUserResponse() => Deserialize<AdminUserItem>();

    [Request("v1/manage/users/update", Name = "adminUpdateUser")]
    public object BuildUpdateUserRequest() => new
    {
        execute = "v1/manage/users/update",
        session = Session,
        deviceInfo = DeviceInfo,
        id = Parameters[0],
        nickName = Parameters[1],
        enabled = Parameters[2],
        isAdministrator = Parameters[3],
        password = Parameters[4]
    };

    [Response("v1/manage/users/update", Name = "adminUpdateUser")]
    public object ParseUpdateUserResponse() => Deserialize<AdminUserItem>();

    [Request("v1/manage/tools/list", Name = "adminTools")]
    public object BuildAdminToolsRequest() => AuthenticatedBody("v1/manage/tools/list");

    [Response("v1/manage/tools/list", Name = "adminTools")]
    public object ParseAdminToolsResponse() => Deserialize<ToolPackageUploadResult[]>();

    [Request("v1/manage/tools/upload", Name = "adminUploadTool")]
    public object BuildAdminUploadToolRequest() => new
    {
        execute = "v1/manage/tools/upload",
        session = Session,
        deviceInfo = DeviceInfo,
        packageBase64 = Parameters[0],
        published = Parameters[1],
        overwrite = Parameters[2]
    };

    [Response("v1/manage/tools/upload", Name = "adminUploadTool")]
    public object ParseAdminUploadToolResponse() => Deserialize<ToolPackageUploadResult>();

    [Request("v1/user/tools/submit", Name = "submitTool")]
    public object BuildSubmitToolRequest() => new
    {
        execute = "v1/user/tools/submit",
        session = Session,
        deviceInfo = DeviceInfo,
        packageBase64 = Parameters[0]
    };

    [Response("v1/user/tools/submit", Name = "submitTool")]
    public object ParseSubmitToolResponse() => Deserialize<ToolPackageUploadResult>();

    [Request("v1/manage/tools/publication", Name = "adminSetPublication")]
    public object BuildAdminSetPublicationRequest() => new
    {
        execute = "v1/manage/tools/publication",
        session = Session,
        deviceInfo = DeviceInfo,
        toolId = Parameters[0],
        version = Parameters[1],
        published = Parameters[2]
    };

    [Response("v1/manage/tools/publication", Name = "adminSetPublication")]
    public object ParseAdminSetPublicationResponse() => Deserialize<ToolPackageUploadResult>();

    [Request("v1/manage/tools/review", Name = "adminReviewTool")]
    public object BuildAdminReviewToolRequest() => new
    {
        execute = "v1/manage/tools/review",
        session = Session,
        deviceInfo = DeviceInfo,
        toolId = Parameters[0],
        version = Parameters[1],
        approved = Parameters[2],
        reviewMessage = Parameters.Length > 3 ? Parameters[3] : null
    };

    [Response("v1/manage/tools/review", Name = "adminReviewTool")]
    public object ParseAdminReviewToolResponse() => Deserialize<ToolPackageUploadResult>();

    [Request("v1/manage/software/list", Name = "adminSoftware")]
    public object BuildAdminSoftwareRequest() => AuthenticatedBody("v1/manage/software/list");

    [Response("v1/manage/software/list", Name = "adminSoftware")]
    public object ParseAdminSoftwareResponse() => Deserialize<SoftwareCatalogItem[]>();

    [Request("v1/manage/software/upsert", Name = "adminUpsertSoftware")]
    public object BuildAdminUpsertSoftwareRequest() => new
    {
        execute = "v1/manage/software/upsert",
        session = Session,
        deviceInfo = DeviceInfo,
        software = Parameters[0]
    };

    [Response("v1/manage/software/upsert", Name = "adminUpsertSoftware")]
    public object ParseAdminUpsertSoftwareResponse() => Deserialize<SoftwareCatalogItem>();

    [Request("v1/manage/software/publication", Name = "adminSetSoftwarePublication")]
    public object BuildAdminSetSoftwarePublicationRequest() => new
    {
        execute = "v1/manage/software/publication",
        session = Session,
        deviceInfo = DeviceInfo,
        softwareId = Parameters[0],
        published = Parameters[1]
    };

    [Response("v1/manage/software/publication", Name = "adminSetSoftwarePublication")]
    public object ParseAdminSetSoftwarePublicationResponse() => Deserialize<SoftwareCatalogItem>();

    [Request("v1/manage/software/upload", Name = "adminUploadSoftware")]
    public object BuildAdminUploadSoftwareRequest() => new
    {
        execute = "v1/manage/software/upload",
        session = Session,
        deviceInfo = DeviceInfo,
        softwareId = Parameters[0],
        channelId = Parameters[1],
        fileName = Parameters[2],
        fileBase64 = Parameters[3]
    };

    [Response("v1/manage/software/upload", Name = "adminUploadSoftware")]
    public object ParseAdminUploadSoftwareResponse() => Deserialize<SoftwareCatalogItem>();

    [Request("v1/manage/software/delete", Name = "adminDeleteSoftware")]
    public object BuildAdminDeleteSoftwareRequest() => new
    {
        execute = "v1/manage/software/delete",
        session = Session,
        deviceInfo = DeviceInfo,
        softwareId = Parameters[0]
    };

    [Response("v1/manage/software/delete", Name = "adminDeleteSoftware")]
    public object ParseAdminDeleteSoftwareResponse() => Deserialize<JsonElement>();

    private object AuthenticatedBody(string execute) => new
    {
        execute,
        session = Session,
        deviceInfo = DeviceInfo
    };

    private T Deserialize<T>() => JsonSerializer.Deserialize<T>(UnescapedResponse, JsonOptions)
                                  ?? throw new JsonException("服务器返回了空数据。");
}
