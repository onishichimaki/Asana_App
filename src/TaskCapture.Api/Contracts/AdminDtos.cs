using System.ComponentModel.DataAnnotations;

namespace TaskCapture.Api.Contracts;

public sealed class UpdateUserAccessRequest
{
    public bool IsActive { get; set; } = true;
    public bool IsAdmin { get; set; }
    public bool RestrictProjects { get; set; }
    [MaxLength(500)] public List<ProjectAccessInput> AllowedProjects { get; set; } = [];
}

public sealed class ProjectAccessInput
{
    [Required, RegularExpression(@"^\d{1,64}$")]
    public string ProjectGid { get; set; } = string.Empty;
    [Required, MaxLength(200)]
    public string ProjectName { get; set; } = string.Empty;
}
