using Jaiz_BulkAccountService.Data;
using Jaiz_BulkAccountService.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using NINServiceReference;
using Oracle.ManagedDataAccess.Client;
using RestSharp;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using static System.Formats.Asn1.AsnWriter;

namespace Jaiz_BulkAccountService
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IConfiguration _configuration;
        private readonly IServiceScopeFactory _scopeFactory;
        //private readonly BulkAccountSolutionDbContext _db;

        public Worker(ILogger<Worker> logger, IConfiguration configuration, /*BulkAccountSolutionDbContext db,*/ IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _configuration = configuration;
            //_db = db;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Bulk Account Worker Started...");
            Console.WriteLine("Bulk Account Worker Started...");

            var interval = _configuration.GetValue<int>("ServiceSettings:PollingIntervalMinutes");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessUploads();
                    _logger.LogInformation("ProcessUploads Completed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled service error");
                    Console.WriteLine($"{ex}, Unhandled service error");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(interval), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("Stagging Service Stopped");
            Console.WriteLine("Stagging Service Stopped");
        }

        public string SendNotificationEmail(string emailaddress, string name, string message)
        {
            try
            {
                string templatePath = Path.Combine(
                    AppContext.BaseDirectory,
                    "Templates",
                    "NotificationMail.htm");

                if (!File.Exists(templatePath))
                {
                    _logger.LogError(
                        "Email template not found: {TemplatePath}",
                        templatePath);

                    return null;
                }

                string mailBody = File.ReadAllText(templatePath);

                mailBody = mailBody.Replace("#FullName#", name);
                mailBody = mailBody.Replace("#Message#", message);

                JaizServiceReference.JaizHelperClient service =
                    new JaizServiceReference.JaizHelperClient();

                JaizServiceReference.EmailObject obj =
                    new JaizServiceReference.EmailObject
                    {
                        Attachment = null,
                        EmailAddress = emailaddress,
                        EmailContent = mailBody,
                        FromAddress = "platform@jaizbankplc.com",
                        HasAttachment = 0,
                        SenderId = "SRVMGT",
                        Subject = "Bulk Account Solution Notification"
                    };

                var response = service.SendEmailViaHelper(obj);

                _logger.LogInformation(
                    "Notification email sent to {Email}",
                    emailaddress);

                return response.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unable to send notification email to {Email}",
                    emailaddress);

                return null;
            }
        }

        // ========================= TIER 1 =========================
        public async Task OpenTier1Accounts(string uploadId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BulkAccountSolutionDbContext>();

            _logger.LogInformation("Tier1 processing started | UploadId={UploadId}", uploadId);


            while (true)
            {
                bool isPaused = db.BulkAccountUpload.Any(m => m.Instancez != "pause" && m.UploadId == uploadId);
                if (isPaused)
                {
                    var accounts = await db.BulkAccount.Where(x => x.Status == "0" && x.UploadId == uploadId).OrderBy(x => x.AccountID).Take(400).ToListAsync();
                    if (!accounts.Any())
                    {
                        _logger.LogInformation("Tier1 processing completed | UploadId={UploadId}", uploadId);
                        break;
                    }

                    foreach (var account in accounts)
                    {
                        try
                        {
                            _logger.LogInformation("Processing Tier1 | UploadId={UploadId}, BVN={BVN}", uploadId, account.Bvn);

                            if (string.IsNullOrWhiteSpace(account.Bvn))
                                continue;

                            var bvnResponse = GetBVNDetails(account.Bvn);
                            if (bvnResponse == null)
                            {
                                await UpdateFailureReason(account.Bvn, "BVN response is null", "0", uploadId);
                                continue;
                            }

                            var validation = ValidateBVN(account, bvnResponse);
                            if (!validation.IsValid)
                            {
                                await UpdateFailureReason(account.Bvn, validation.Message, validation.Code, uploadId);
                                continue;
                            }

                            var request = BuildAccountRequest(account, bvnResponse);
                            var response = await CreateAccountAsync(request, uploadId);

                            if (response == null)
                                continue;

                            account.AccountNo = response.accountNo;
                            account.Cif = response.cif;
                            account.Status = "2";
                            account.FailureReason = "";
                            account.DateOpened = DateTime.Now;

                            await db.SaveChangesAsync();

                            //await db.BulkAccountUpload.Where(x => x.Instancez != "pause" && x.UploadId == uploadId).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.CreatedCount, x => x.CreatedCount + 1));

                            _logger.LogInformation("Tier1 account created | UploadId={UploadId}, BVN={BVN}, AccountNo={AccountNo}", uploadId, account.Bvn, response.accountNo);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Tier1 processing error | UploadId={UploadId}, BVN={BVN}", uploadId, account.Bvn);
                        }
                    }
                }
                else
                {
                    break;
                }
            }

        }

        // ========================= SAVINGS =========================
        public async Task OpenSavingsAccounts(string uploadId)
        {
            _logger.LogInformation("Savings processing started | UploadId={UploadId}", uploadId);

            while (true)
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BulkAccountSolutionDbContext>();

                bool isPaused = db.BulkAccountUpload.Any(m => m.Instancez != "pause" && m.UploadId == uploadId);
                if (isPaused)
                {
                    var accounts = await db.BulkAccount.Where(x => x.Status == "0" && x.UploadId == uploadId).OrderBy(x => x.AccountID).Take(400).ToListAsync();
                    if (!accounts.Any())
                    {
                        _logger.LogInformation("Savings processing completed | UploadId={UploadId}", uploadId);
                        break;
                    }

                    foreach (var account in accounts)
                    {
                        try
                        {
                            _logger.LogInformation("Processing Savings | UploadId={UploadId}, BVN={BVN}", uploadId, account.Bvn);

                            var bvn = GetBVNDetails(account.Bvn!);
                            var bvnResult = ValidateBVN(account, bvn);
                            if (!bvnResult.IsValid)
                            {
                                await UpdateFailureReason(account.Bvn!, bvnResult.Message, bvnResult.Code, uploadId);
                                continue;
                            }

                            var nin = GetNINDetails(account.NIN!);
                            var ninResult = ValidateNIN(account, nin);
                            if (!ninResult.IsValid)
                            {
                                await UpdateFailureReason(account.Bvn, ninResult.Message, ninResult.Code, uploadId);
                                continue;
                            }

                            var request = BuildAccountRequest(account, bvn);
                            var response = await CreateAccountAsync(request, uploadId);

                            if (response == null)
                                continue;

                            PlaceOnPND(response.accountNo);

                            account.AccountNo = response.accountNo;
                            account.Cif = response.cif;
                            account.Status = "2";
                            account.FailureReason = "";
                            account.DateOpened = DateTime.Now;

                            await db.SaveChangesAsync();

                            //await db.BulkAccountUpload.Where(x => x.Instancez != "pause" && x.UploadId == uploadId).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.CreatedCount, x => x.CreatedCount + 1));

                            _logger.LogInformation("Savings account created | UploadId={UploadId}, BVN={BVN}, AccountNo={AccountNo}", uploadId, account.Bvn, response.accountNo);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Savings processing error | UploadId={UploadId}, BVN={BVN}", uploadId, account.Bvn);
                        }
                    }
                }
                else
                {
                    break;
                }



            }
        }

        // ========================= KIDS =============================
        public async Task OpenKidsAccounts(string uploadId)
        {
            _logger.LogInformation("Tier1 processing started | UploadId={UploadId}", uploadId);

            while (true)
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BulkAccountSolutionDbContext>();

                bool isPaused = db.BulkAccountUpload.Any(m => m.Instancez != "pause" && m.UploadId == uploadId);
                if (isPaused)
                {
                    var accounts = await db.BulkAccount.Where(item => item.Status == "0" && item.UploadId == uploadId).OrderBy(x => x.AccountID).Take(400).ToListAsync();
                    if (!accounts.Any())
                    {
                        _logger.LogInformation("Kids processing completed | UploadId={UploadId}", uploadId);
                        break;
                    }

                    foreach (var account in accounts)
                    {
                        try
                        {
                            _logger.LogInformation("Processing Kids | UploadId={UploadId}, BVN={BVN}", uploadId, account.Bvn);

                            if (string.IsNullOrEmpty(account.Bvn)) continue;

                            var bvnResponse = GetBVNDetails(account.Bvn);
                            if (bvnResponse == null)
                            {
                                await UpdateFailureReason(account.Bvn, "BVN response is null", "0", uploadId);

                                _logger.LogInformation("BVN response is null | UploadId={UploadId}, BVN={BVN}", uploadId, account.Bvn);
                            }
                            else
                            {
                                account.Sex = account.Sex?.ToUpper() == "MALE" ? "M" : "F";
                                account.Title = account.Sex == "M" ? 23 : 24;

                                if (string.IsNullOrWhiteSpace(account.MktByID))
                                    account.MktByID = "99999007";

                                var dob = DateTime.Parse(account.Dob);
                                string formattedDob = dob.ToString("yyyy-MM-dd");


                                AccountOpeningRequest request = new AccountOpeningRequest
                                {
                                    accountName = $"{account.FName} {account.OName} {account.SName}",
                                    addref = account.Address,
                                    address = account.Address,
                                    branchcode = account.BranchCode?.ToString(),
                                    cif = "",
                                    curencycode = "566",
                                    secondname = account.OName,
                                    firstname = account.FName,
                                    lastname = account.SName,
                                    glcode = account.GlCode,
                                    idtype = account.IDType,
                                    idno = account.IDNumber,
                                    idexpirydate = "9999-01-01",
                                    bvn = account.Bvn,
                                    marital = "S",
                                    sex = account.Sex,
                                    telephone = account.Phone,
                                    dob = formattedDob,
                                    title = account.Title,
                                    ecosector = "8",
                                    division = 22,
                                    dept = 223,
                                    externalAccountNo = "",
                                    externalPartyCode = "",
                                    marketedbyid = account.MktByID,
                                    marketedforid = account.MktForID,
                                    channel = "BulkAcctSol"
                                };
                                var response = await CreateAccountAsync(request, uploadId);
                                if (response != null)
                                {
                                    PlaceOnPND(response.accountNo);

                                    account.AccountName = request.accountName;
                                    account.FName = request.firstname;
                                    account.OName = request.lastname;
                                    account.Phone = request.telephone;
                                    account.AccountNo = response.accountNo;
                                    account.Cif = response.cif;
                                    account.FailureReason = "";
                                    account.Status = "2";
                                    account.DateOpened = DateTime.Now;

                                    await db.SaveChangesAsync();

                                    //await db.BulkAccountUpload.Where(x => x.Instancez != "pause" && x.UploadId == uploadId).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.CreatedCount, x => x.CreatedCount + 1));

                                    _logger.LogInformation("Kids account created | UploadId={UploadId}, BVN={BVN}, AccountNo={AccountNo}", uploadId, account.Bvn, response.accountNo);

                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Kids processing error | UploadId={UploadId}, BVN={BVN}", uploadId, account.Bvn);
                        }
                    }
                }
                else
                {
                    break;
                }

            }
        }

        // ========================= DB HELPERS =========================
        private async Task UpdateFailureReason(string bvn, string reason, string status, string uploadId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BulkAccountSolutionDbContext>();

            await db.BulkAccount.Where(x => x.Bvn == bvn && x.UploadId == uploadId).ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.FailureReason, reason)
                    .SetProperty(x => x.Status, status));

            _logger.LogWarning("Account failed | UploadId={UploadId}, BVN={BVN}, Reason={Reason}", uploadId, bvn, reason);
        }

        private async Task HandleAccountCreationFailures(string responseContent, string bvn, string uploadID, string glcode)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BulkAccountSolutionDbContext>();

                if (responseContent.Contains("Invalid BVN"))
                {
                    await UpdateFailureReason(bvn, "Invalid BVN", "4", uploadID);
                }
                else if (responseContent.Contains("Invalid DOB"))
                {
                    await UpdateFailureReason(bvn, "Account holder must be 18 years and above", "6", uploadID);
                }
                else if (responseContent.ToUpper().Contains("TIERED"))
                {
                    var (cif, acct) = await GetAccountOpenedTodayByBVN(bvn, glcode);

                    var account = db.BulkAccount.Where(a => a.Bvn == bvn && a.UploadId == uploadID).FirstOrDefault();
                    account.AccountNo = acct;
                    account.Cif = cif;
                    account.Status = "6";
                    account.FailureReason = "Cust has acct. Tiered acct can't be created. See account number and CIF";

                    await db.SaveChangesAsync();
                }
                else if (responseContent.Contains("This customer has an existing savings"))
                {
                    var (cif, acct) = await GetAccountOpenedTodayByBVN(bvn, glcode);

                    var account = db.BulkAccount.Where(a => a.Bvn == bvn && a.UploadId == uploadID).FirstOrDefault();
                    account.AccountNo = acct;
                    account.Cif = cif;
                    account.Status = "6";
                    account.FailureReason = "This customer has an existing savings account. See account number and CIF.";

                    await db.SaveChangesAsync();
                }
                else if (responseContent.Contains("problem with cif"))
                {
                    var (cif, acct) = await GetAccountOpenedTodayByBVN(bvn, glcode);

                    if (!string.IsNullOrEmpty(acct))
                    {
                        var account = db.BulkAccount.Where(a => a.Bvn == bvn && a.UploadId == uploadID).FirstOrDefault();

                        account.AccountNo = acct;
                        account.Cif = cif;
                        account.Status = "2";
                        account.FailureReason = "";

                        await db.SaveChangesAsync();
                    }
                    else
                    {
                        await UpdateFailureReason(bvn, "Error creating account, problem with cif", "6", uploadID);
                    }
                }
                else if (responseContent.Contains("Address cannot be empty"))
                {
                    await UpdateFailureReason(bvn, "Address cannot be empty", "6", uploadID);
                }
                else if (responseContent.Contains("Telephone cannot be empty"))
                {
                    await UpdateFailureReason(bvn, "Telephone not on BVN", "6", uploadID);
                }
                else
                {
                    var (cif, acct) = await GetAccountOpenedTodayByBVN(bvn, glcode);

                    if (!string.IsNullOrEmpty(acct))
                    {
                        var account = db.BulkAccount.Where(a => a.Bvn == bvn && a.UploadId == uploadID).FirstOrDefault();

                        account.AccountNo = acct;
                        account.Cif = cif;
                        account.Status = "2";
                        account.FailureReason = "";

                        await db.SaveChangesAsync();
                    }
                    else
                    {
                        await UpdateFailureReason(bvn, "Unable to create account, try again later.", "0", uploadID);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to update failure reason BVN={BVN}", bvn);
            }
        }
        // ========================= API =========================
        private async Task<CreateAccountResponse?> CreateAccountAsync(AccountOpeningRequest request, string uploadId)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BulkAccountSolutionDbContext>();

                var client = new RestClient(_configuration["appConfiguration:AccountOpeningURL"]);
                var authToken = _configuration["appConfiguration:AuthToken"];

                var restRequest = new RestRequest("AccountCreationReview", Method.Post)
                    .AddHeader("content-type", "application/json")
                    .AddHeader("Authorization", authToken)
                    .AddJsonBody(request);

                var response = await client.ExecuteAsync(restRequest);

                if (!response.IsSuccessful)
                {
                    var (cif, acct) = await GetAccountOpenedTodayByBVN(request.bvn, request.glcode);

                    if (!string.IsNullOrEmpty(acct))
                    {

                        var account = db.BulkAccount.Where(a => a.Bvn == request.bvn && a.UploadId == uploadId).FirstOrDefault();

                        account!.AccountNo = acct;
                        account.Cif = cif;
                        account.Status = "2";
                        account.FailureReason = "";

                        await db.SaveChangesAsync();

                        return null;
                    }
                    else
                    {
                        await UpdateFailureReason(request.bvn, "API failure", "0", uploadId);
                        return null;
                    }
                }

                var result = JsonConvert.DeserializeObject<CreateAccountResponse>(response.Content!);

                if (result?.responseCode != "00")
                {
                    await HandleAccountCreationFailures(response.Content!, request.bvn, uploadId, request.glcode);
                    return null;
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateAccount API error | UploadId={UploadId}, BVN={BVN}", uploadId, request.bvn);

                return null;
            }
        }

        // ========================= UTILITIES =========================
        private void PlaceOnPND(string accountNo)
        {
            try
            {
                var url = $"{_configuration["appConfiguration:PlaceAccountPNDURL"]}{accountNo}/91/BulkAcct";
                WebRequest.Create(url).GetResponse();
                _logger.LogInformation("PND placed | AccountNo={AccountNo}", accountNo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PND failed | AccountNo={AccountNo}", accountNo);
            }
        }

        public NINValidationResult ValidateNIN(Account account, NINResponse nin)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(nin.firstName))
                    return NINValidationResult.Failure($"This NIN: {account.IDNumber} is invalid.", "4");

                if (!string.Equals(account.FName?.Trim(), nin.firstName?.Trim(), StringComparison.OrdinalIgnoreCase))
                    return NINValidationResult.Failure($"Supplied firstname [{account.FName}] is different from firstname [{nin.firstName}] on NIN.", "3");

                if (!string.Equals(account.OName?.Trim(), nin.middleName?.Trim(), StringComparison.OrdinalIgnoreCase))
                    return NINValidationResult.Failure($"Supplied middlename [{account.OName}] is different from middlename [{nin.middleName}] on NIN.", "4");

                if (!string.Equals(account.SName?.Trim(), nin.surName?.Trim(), StringComparison.OrdinalIgnoreCase))
                    return NINValidationResult.Failure($"Supplied surname [{account.SName}] is different from surname [{nin.surName}] on NIN.", "5");

                var dobFormats = new[] { "dd-MM-yyyy", "dd-MMM-yyyy", "dd-MMM-yy" };

                var dobAccount = DateTime.ParseExact(account.Dob, dobFormats, CultureInfo.InvariantCulture, DateTimeStyles.None);
                var dobNin = DateTime.ParseExact(nin.birthDate, dobFormats, CultureInfo.InvariantCulture, DateTimeStyles.None);

                if (dobAccount != dobNin)
                {
                    return NINValidationResult.Failure(
                        $"Supplied DOB [{account.Dob}] is different from DOB [{nin.birthDate}] on NIN.", "7");
                }

                var normalizedGender = account.Sex?.Trim().ToLower() switch
                {
                    "m" or "male" => "m",
                    "f" or "female" => "f",
                    _ => ""
                };

                if (!string.Equals(normalizedGender, nin.gender?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return NINValidationResult.Failure(
                        $"Supplied gender [{account.Sex}] is different from gender [{nin.gender}] on NIN.", "8");
                }
                return NINValidationResult.Success();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ValidateNIN error | NIN={NIN}, Message={Message}", nin, ex.Message);
            }
            return NINValidationResult.Failure("", "");
        }

        public BvnValidationResult ValidateBVN(Account account, Models.BVNResponse bvn)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(bvn.firstName))
                    return BvnValidationResult.Failure($"This BVN: {account.Bvn} is invalid.", "4");

                if (!string.Equals(account.FName?.Trim(), bvn.firstName?.Trim(), StringComparison.OrdinalIgnoreCase))
                    return BvnValidationResult.Failure($"Supplied firstname [{account.FName}] is different from firstname [{bvn.firstName}] on BVN.", "3");

                if (!string.Equals(account.OName?.Trim(), bvn.middleName?.Trim(), StringComparison.OrdinalIgnoreCase))
                    return BvnValidationResult.Failure($"Supplied middlename [{account.OName}] is different from middlename [{bvn.middleName}] on BVN.", "4");

                if (!string.Equals(account.SName?.Trim(), bvn.lastName?.Trim(), StringComparison.OrdinalIgnoreCase))
                    return BvnValidationResult.Failure($"Supplied surname [{account.SName}] is different from surname [{bvn.lastName}] on BVN.", "5");

                var dobFormats = new[] { "dd-MM-yyyy", "dd-MMM-yyyy", "dd-MMM-yy" };

                var dobAccount = DateTime.ParseExact(account.Dob, dobFormats, CultureInfo.InvariantCulture, DateTimeStyles.None);
                var dobBvn = DateTime.ParseExact(bvn.dateOfBirth, dobFormats, CultureInfo.InvariantCulture, DateTimeStyles.None);

                if (dobAccount != dobBvn)
                {
                    return BvnValidationResult.Failure(
                        $"Supplied DOB [{account.Dob}] is different from DOB [{bvn.dateOfBirth}] on BVN.", "7");
                }

                var normalizedGender = account.Sex?.Trim().ToUpper() switch
                {
                    "M" or "MALE" => "Male",
                    "F" or "FEMALE" => "Female",
                    _ => ""
                };

                if (!string.Equals(normalizedGender, bvn.gender?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return BvnValidationResult.Failure(
                        $"Supplied gender [{account.Sex}] is different from gender [{bvn.gender}] on BVN.", "8");
                }

                return BvnValidationResult.Success();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ValidateBVN error | BVN={BVN}, Message={Message}", bvn, ex.Message);
            }
            return BvnValidationResult.Failure("", "");
        }

        public BvnValidationResult ValidateBVN_2(string FName, string OName, string SName, string Dob, string Sex, string BVN, Models.BVNResponse bvn)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(bvn.firstName))
                    return BvnValidationResult.Failure($"This BVN: {BVN} is invalid.", "4");

                if (!string.Equals(FName?.Trim(), bvn.firstName?.Trim(), StringComparison.OrdinalIgnoreCase))
                    return BvnValidationResult.Failure($"Supplied firstname [{FName}] is different from firstname [{bvn.firstName}] on BVN.", "3");

                if (!string.Equals(OName?.Trim(), bvn.middleName?.Trim(), StringComparison.OrdinalIgnoreCase))
                    return BvnValidationResult.Failure($"Supplied middlename [{OName}] is different from middlename [{bvn.middleName}] on BVN.", "4");

                if (!string.Equals(SName?.Trim(), bvn.lastName?.Trim(), StringComparison.OrdinalIgnoreCase))
                    return BvnValidationResult.Failure($"Supplied surname [{SName}] is different from surname [{bvn.lastName}] on BVN.", "5");

                var dobFormats = new[] { "dd-MM-yyyy", "dd-MMM-yyyy", "dd-MMM-yy" };

                var dobAccount = DateTime.ParseExact(Dob, dobFormats, CultureInfo.InvariantCulture, DateTimeStyles.None);
                var dobBvn = DateTime.ParseExact(bvn.dateOfBirth, dobFormats, CultureInfo.InvariantCulture, DateTimeStyles.None);

                if (dobAccount != dobBvn)
                {
                    return BvnValidationResult.Failure(
                        $"Supplied DOB [{Dob}] is different from DOB [{bvn.dateOfBirth}] on BVN.", "7");
                }

                var normalizedGender = Sex?.Trim().ToUpper() switch
                {
                    "M" or "MALE" => "Male",
                    "F" or "FEMALE" => "Female",
                    _ => ""
                };

                if (!string.Equals(normalizedGender, bvn.gender?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return BvnValidationResult.Failure(
                        $"Supplied gender [{Sex}] is different from gender [{bvn.gender}] on BVN.", "8");
                }

                return BvnValidationResult.Success();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ValidateBVN error | BVN={BVN}, Message={Message}", bvn, ex.Message);
            }
            return BvnValidationResult.Failure("", "");
        }

        private NINResponse GetNINDetails(string nin)
        {
            NINResponse ninResponse = null;
            try
            {
                var jzService_1 = new JaizHelperSoapClient(0);
                var response = jzService_1.SearchNIMCAsync(nin).GetAwaiter().GetResult();

                var data = response.data?.FirstOrDefault();
                if (data != null)
                {
                    ninResponse = new NINResponse
                    {
                        firstName = data.firstname,
                        middleName = data.middlename,
                        surName = data.surname,
                        birthDate = data.birthdate,
                        gender = data.gender
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetNINDetails API error | NIN={NIN}, Message={Message}", nin, ex.Message);
            }

            return ninResponse;
        }
        private Models.BVNResponse GetBVNDetails(string bvn)
        {
            string result = string.Empty;
            try
            {
                var BVNCheckURL = _configuration["appConfiguration:BVNCheckURL"];
                string postData = JsonConvert.SerializeObject(new { bvn });
                var request = (HttpWebRequest)WebRequest.Create(BVNCheckURL);
                request.Method = "POST";
                request.ContentType = "application/json";

                using (var streamWriter = new StreamWriter(request.GetRequestStream()))
                {
                    streamWriter.Write(postData);
                }

                var response = (HttpWebResponse)request.GetResponse();
                using (var streamReader = new StreamReader(response.GetResponseStream()))
                {
                    result = streamReader.ReadToEnd();
                }

                return JsonConvert.DeserializeObject<Models.BVNResponse>(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetBVNDetails API error | BVN={BVN}, Message={Message}", bvn, ex.Message);
            }
            return JsonConvert.DeserializeObject<Models.BVNResponse>(result);
        }
        public class BvnValidationResult
        {
            public bool IsValid { get; set; }
            public string Message { get; set; }
            public string Code { get; set; }

            public static BvnValidationResult Success() => new BvnValidationResult { IsValid = true };
            public static BvnValidationResult Failure(string message, string code) => new BvnValidationResult { IsValid = false, Message = message, Code = code };
        }
        public class NINValidationResult
        {
            public bool IsValid { get; set; }
            public string Message { get; set; }
            public string Code { get; set; }

            public static NINValidationResult Success() => new NINValidationResult { IsValid = true };
            public static NINValidationResult Failure(string message, string code) =>
                new NINValidationResult { IsValid = false, Message = message, Code = code };
        }

        public async Task<(string, string)> GetAccountOpenedTodayByBVN(string bvn, string glcode)
        {
            var _cif = "";
            var _accountNumber = "";

            //Check if an Account has been opened for this BVN today
            using (OracleConnection OracConnection = new OracleConnection(_configuration["ConnectionStrings:OracleDbConn"]))
            {
                var Oraccmd = new OracleCommand("SELECT cif_sub_no, A.ADDITIONAL_REFERENCE NUBAN FROM JZPORTAL.BIODATA B JOIN IMAL.AMF A ON B.CUSTOMERID = A.CIF_SUB_NO where B.biometricid = '" + bvn + "' and A.gl_code = '" + glcode + "'", OracConnection);
                await OracConnection.OpenAsync();

                using (var OracReader = Oraccmd.ExecuteReader())
                {
                    while (OracReader.Read())
                    {
                        _cif = OracReader[0].ToString()!;
                        _accountNumber = OracReader[1].ToString()!;
                    }
                }
            }

            return (_cif, _accountNumber);
        }
        public AccountOpeningRequest BuildAccountRequest(Account account, Models.BVNResponse bvn)
        {
            try
            {
                // Default missing fields from BVN if needed
                if (string.IsNullOrWhiteSpace(account.Phone))
                    account.Phone = bvn.phoneNumber1;

                account.Sex = bvn.gender?.ToUpper() == "MALE" ? "M" : "F";
                account.Title = account.Sex == "M" ? 23 : 24;

                if (string.IsNullOrWhiteSpace(account.MktByID))
                    account.MktByID = "99999007";

                var dob = DateTime.Parse(bvn.dateOfBirth);
                string formattedDob = dob.ToString("yyyy-MM-dd");

                return new AccountOpeningRequest
                {
                    accountName = $"{bvn.firstName} {bvn.middleName} {bvn.lastName}",
                    addref = account.Address,
                    address = account.Address,
                    branchcode = account.BranchCode?.ToString(),
                    cif = "",
                    curencycode = "566",
                    secondname = bvn.middleName,
                    firstname = bvn.firstName,
                    lastname = bvn.lastName,
                    glcode = account.GlCode,
                    idtype = account.IDType,
                    idno = account.IDNumber,
                    idexpirydate = "9999-01-01",
                    bvn = account.Bvn,
                    marital = "S",
                    sex = account.Sex,
                    telephone = account.Phone,
                    dob = formattedDob,
                    title = account.Title,
                    ecosector = "8",
                    division = 22,
                    dept = 223,
                    externalAccountNo = "",
                    externalPartyCode = "",
                    marketedbyid = account.MktByID,
                    marketedforid = account.MktForID,
                    channel = "BulkAcctSol"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BuildAccountRequest error | BVN={BVN}, Message={Message}", bvn, ex.Message);
            }
            return null;
        }

        public async Task ProcessUploads()
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BulkAccountSolutionDbContext>();

                var upload = await db.BulkAccountUpload.FirstOrDefaultAsync(x => x.Status == "Approved" && x.Instancez == "10" || x.Status == "Processing" && x.Instancez == "10" || x.Status == "Failed" && x.Instancez == "10");
                if (upload != null)
                {
                    upload.Status = "Processing";
                    db.SaveChanges();
                    _logger.LogInformation("Initiating UploadId={UploadId}, AccountType={Type}", upload!.UploadId, upload.AccountType);
                }

                try
                {
                    if (upload != null)
                    {
                        if (upload.AccountType == "tier1")
                        {
                            await OpenTier1Accounts(upload.UploadId!);
                            SendNotificationEmail(upload.InitiatorEmail!, "Initiator", "Your bulk account creation is complete.");
                        }
                        else if (upload.AccountType == "savings" || upload.AccountType == "salary")
                        {
                            await OpenSavingsAccounts(upload.UploadId!);
                            SendNotificationEmail(upload.InitiatorEmail!, "Initiator", "Your bulk account creation is complete.");
                        }
                        else if (upload.AccountType == "kids")
                        {
                            await OpenKidsAccounts(upload.UploadId!);
                            SendNotificationEmail(upload.InitiatorEmail!, "Initiator", "Your bulk account creation is complete.");
                        }

                        upload.Status = "Completed";
                        await db.SaveChangesAsync();

                        _logger.LogInformation("Completed UploadId={UploadId}", upload.UploadId);
                    }
                }
                catch (Exception ex)
                {
                    upload!.Status = "Failed";
                    await db.SaveChangesAsync();

                    _logger.LogError(ex, "Failed processing for upload batch UploadId={UploadId}", upload.UploadId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ProcessUploads Failure");
            }

        }

    }
}
