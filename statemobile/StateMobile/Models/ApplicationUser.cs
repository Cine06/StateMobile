using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Migrations;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace StateMobile.Models;

public class ApplicationUser : IdentityUser
{
    [PersonalData]
    public DateTime DOB { get; set; }

    [PersonalData]
    [StringLength(100)]
    public string FirstName { get; set; }
    [PersonalData]
    [StringLength(100)]
    public string LastName { get; set; }

    [PersonalData]
    public byte[]? ProfilePicture { get; set; }

    [PersonalData]
    public int DeptCode { get; set; }

    [PersonalData]
    public string PW { get; set; }
}
