using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Jaiz_BulkAccountService.Models
{
    public class BulkAccountUpload
    {
        [Key]
        public string? UploadId { get; set; }
        public string? AccountType { get; set; }
        public string? StaffID { get; set; }
        public string? BranchCode { get; set; }
        public string? Status { get; set; }
        public string? FilePath { get; set; }
        public DateTime UploadDate { get; set; }
        public string? InitiatorEmail { get; set; }
        public string? RejectionReason { get; set; }
        public string? Instancez { get; set; }
        public int? CreatedCount { get; set; }
        public int? UploadedCount { get; set; }
    }
}
