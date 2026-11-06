using StudentSync.Domain.Identity;
using StudentSync.Domain.People;

namespace StudentSync.UnitTests;

/// <summary>Addendum B/C invariants that live in the domain: admin-controlled account states and who may touch learning needs.</summary>
public sealed class LearningProfileRulesTests
{
    [Fact]
    public void Person_status_has_no_pending_approval_and_includes_suspended()
    {
        Assert.DoesNotContain("PendingApproval", Enum.GetNames<PersonStatus>());
        Assert.Equal(["Active", "Inactive", "Archived", "Suspended"], Enum.GetNames<PersonStatus>());
    }

    [Theory]
    [InlineData(UserRole.InstitutionAdmin, true, false)]
    [InlineData(UserRole.Teacher, true, false)]
    [InlineData(UserRole.Student, false, true)]
    [InlineData(UserRole.Parent, false, false)]
    [InlineData(UserRole.SuperAdmin, false, false)]
    public void Learning_need_permissions_follow_the_role_matrix(UserRole role, bool manage, bool self)
    {
        Assert.Equal(manage, RolePermissions.Has(role, Permissions.LearningNeedsManage));
        Assert.Equal(self, RolePermissions.Has(role, Permissions.LearningNeedsSelf));
    }

    [Fact]
    public void Only_admins_manage_users_so_only_admins_create_students_and_reset_passwords()
    {
        Assert.True(RolePermissions.Has(UserRole.InstitutionAdmin, Permissions.UsersManage));
        Assert.False(RolePermissions.Has(UserRole.Teacher, Permissions.UsersManage));
        Assert.False(RolePermissions.Has(UserRole.Student, Permissions.UsersManage));
    }
}
