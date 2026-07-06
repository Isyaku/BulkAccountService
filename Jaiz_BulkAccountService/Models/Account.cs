using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Jaiz_BulkAccountService.Models
{
    public class Account
    {
        [Key]
        public int AccountID { get; set; }
        public string? FName { get; set; }
        public string? SName { get; set; }
        public string? OName { get; set; }
        public string? AccountName { get; set; }
        public string? Sex { get; set; }
        public string? Dob { get; set; }
        public string? Address { get; set; }
        [Required]
        public string? Phone { get; set; }
        [Required]
        public int Title { get; set; }
        [Required]
        public string? Bvn { get; set; }
        public string? AccountNo { get; set; }
        public string? Cif { get; set; }
        public DateTime? DateOpened { get; set; }
        [Required]
        public string? BranchCode { get; set; }
        public string? Status { get; set; }
        public string? FailureReason { get; set; }
        public string? IDType { get; set; }
        public string? IDNumber { get; set; }
        public string? Email { get; set; }
        [Required]
        public string? GlCode { get; set; }
        public string? MktByID { get; set; }
        public string? MktForID { get; set; }
        public string? OtherField { get; set; }
        public string? UploadId { get; set; }
        public string? NIN { get; set; }
    }
}
