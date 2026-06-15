using System.Collections.Generic;
using Postgrest.Attributes;
using Postgrest.Models;

namespace RfidScanner.Models;

[Table("users")]
public class UserModel : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; } = string.Empty;

    [Column("createdDate")]
    public string CreatedDate { get; set; } = string.Empty;

    [Column("updatedDate")]
    public string UpdatedDate { get; set; } = string.Empty;

    [Column("userName")]
    public string UserName { get; set; } = string.Empty;

    [Column("companyName")]
    public string CompanyName { get; set; } = string.Empty;

    [Column("email")]
    public string Email { get; set; } = string.Empty;

    [Column("mobile")]
    public long Mobile { get; set; }

    [Column("password")]
    public string Password { get; set; } = string.Empty;

    [Column("role")]
    public int Role { get; set; } // 0 = Admin, 1 = Owner, 2 = Manager, 3 = Staff

    [Column("status")]
    public string Status { get; set; } = "Active";

    [Column("isDeleted")]
    public bool IsDeleted { get; set; }

    [Column("profileImage")]
    public string? ProfileImage { get; set; }

    [Column("userPermissions")]
    public List<string> UserPermissions { get; set; } = new();

    [Column("parentId")]
    public string? ParentId { get; set; }

    [Column("ownerId")]
    public string? OwnerId { get; set; }

    public string RoleName => Role switch
    {
        0 => "Admin",
        1 => "Owner",
        2 => "Manager",
        3 => "Staff",
        _ => "Unknown"
    };
}
