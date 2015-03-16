using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.IO;
using System.Net.Mail;
using System.Net.Mime;
using System.Web;
using System.Web.Mvc;
using System.Configuration;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Diagnostics;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;
using MvcWebRole1;
using System.Globalization;

namespace MvcWebRole1.Controllers
{
    public class HomeController : Controller
    {
      
        //email id & password to send the activation id.
        const string FROM_EMAIL_ADDRESS = "your email id";
        const string FROM_PASSWORD = "password";

        const string EXCHANGE_BASE_URL = "http://apps.exchange.autodesk.com/";

        //database
        private static CloudStorageAccount cloudStorageAccount = CloudStorageAccount.Parse(RoleEnvironment.GetConfigurationSettingValue("StorageConnectionString"));
        
        //factory
        private static DBModelFactory _dataModelFactory = null;

        public ActionResult Index()
        {
            _dataModelFactory = new DBModelFactory(cloudStorageAccount.CreateCloudTableClient());

            // Access the Azure Table Storage to access the data related to the AutoCAD plugins
            IEnumerable<DBModel> azuredata = _dataModelFactory.FetchAll();

            // Iterate through the model and populate the collection that we can provide to the view.
            foreach (DBModel adm in azuredata)
            {
                if (adm != null)
                {
                    String _timeFormat = "dd-MM-yyyy hh:mm:ss tt";
                    DateTime currentDateTimeStamp = DateTime.Now;
                    DateTime dbStoredExpiryTimeStamp = DateTime.Now;
                    double timeRemaining = 0.0;
                    if (DateTime.TryParseExact(adm.ExpiryDateTimeStamp, _timeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out dbStoredExpiryTimeStamp))
                    {
                        if (dbStoredExpiryTimeStamp > currentDateTimeStamp)
                            timeRemaining = (dbStoredExpiryTimeStamp - currentDateTimeStamp).TotalMinutes;
                    }

                    DateTime dbStoredStartTimeStamp = DateTime.Now;
                    double totalTrialDuration = 0.0;
                    if (DateTime.TryParseExact(adm.StartDateTimeStamp, _timeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out dbStoredStartTimeStamp))
                    {
                        totalTrialDuration = (dbStoredExpiryTimeStamp - dbStoredStartTimeStamp).TotalMinutes;
                    }
                }
            }

            return View();
        }

        //delete all command - helper function to clean the database
        //for tetsing only.
        //http://adntermlicensing.cloudapp.net/home/deleteAll?value=Autodesk&id=Test

        public void deleteAll(string value, string id)
        {
            string keywordToDelete = "Autodesk";
            if (string.Compare(keywordToDelete, value, true) == 0)
            {
                _dataModelFactory.deleteAll();
            }
        }

        //Function to activate the App.
        //activationId - id sent to user on purchasing of the App
        //AppId - id of the App from exchange store
        //machineId - Machine id or machine code of the computer where user wants to use the App, for machining locking
        public JsonResult ActivateApp(string activationId, string AppId, string machineId)
        {
            // Validation
            if (String.IsNullOrEmpty(activationId) || String.IsNullOrEmpty(AppId) || String.IsNullOrEmpty(machineId))
            {
                return Json(new { id =4, value = "Invalid input" }, JsonRequestBehavior.AllowGet);
            }

            string sExpiryDateTimeStamp = "";
            string buyerEmail = "";
            //active the App
            int nReturn = _dataModelFactory.activateApp(activationId, AppId, machineId, ref sExpiryDateTimeStamp, ref buyerEmail);

            if (nReturn == 1)
            {
                //more then 1 record in table
                return Json(new { id = 1, value = "Unknown error" }, JsonRequestBehavior.AllowGet); 
            }

            if (nReturn == 3)
            {
                //No recaord for given data
                return Json(new { id = 3, value = "Activation failed, Invalid activation code" }, JsonRequestBehavior.AllowGet);
            }

            if (nReturn == 2)
            {
                return Json(new { id = 2, value = "App is already activated in a different machine" }, JsonRequestBehavior.AllowGet);
            }

            //send email...
            //send a email with new sExpiryDateTimeStamp.
            string bodyTemplate = @"Hi {0},<br/><br/>Thank you for renewing the subscribing to App! <br/><br/> Now, your subscription will end by {1} <br/><br/><br/>Thanks,<br/>Autodesk App Team.";
            string body = string.Format(bodyTemplate, "", sExpiryDateTimeStamp);
            string subject = "Autodesk Exchange store: Subscription activated for App";
            SendEmail(buyerEmail, subject, body);


            return Json(new { id = 0, value = "Activation successful" }, JsonRequestBehavior.AllowGet);
        }

        //Function to check the Entitlement of the App.
        //activationId - id sent to user on purchasing of the App
        //AppId - id of the App from exchange store
        //machineId - Machine id or machine code of the computer where user wants to use the App, for machining locking
        //return value is 0 - means user can use the App.
        public JsonResult CheckAppEntitlement(string activationId, string AppId, string machineId)
        {
            if (String.IsNullOrEmpty(activationId) || String.IsNullOrEmpty(AppId) || String.IsNullOrEmpty(machineId))
            {
                return Json(new { id =6, value = "Invalid input" }, JsonRequestBehavior.AllowGet);
            }

            string expiryDateTimeStamp = "";
            int nReturn = _dataModelFactory.CheckAppEntitlement(activationId, AppId, machineId, ref expiryDateTimeStamp);

            if (nReturn == 0)
            {
                //more then 1 record in table
                return Json(new { id = 0, value = "OK" }, JsonRequestBehavior.AllowGet);
            }
            else if (nReturn == 1)
            {
                //more then 1 record in table
                return Json(new { id = 1, value = "Unknown error" }, JsonRequestBehavior.AllowGet);
            }

            else if (nReturn == 2)
            {
                //more then 1 record in table
                return Json(new { id = 2, value = "App is already activated in a different machine" }, JsonRequestBehavior.AllowGet);
            }

            else if (nReturn == 3)
            {
                return Json(new { id = 3, value = "Subscription expired, please renew the Subscription" }, JsonRequestBehavior.AllowGet);
            }
            else if (nReturn == 4)
            {
                //no record
                return Json(new { id = 4, value = "Invalid activation code" }, JsonRequestBehavior.AllowGet);
            }

            return Json(new { id = 5, value = "Unknown error" }, JsonRequestBehavior.AllowGet);
        }
        
        //function to send the email.
        private static void SendEmail(string toEmail, string subject, string body)
        {
            try
            {
                var fromAddress = new MailAddress(FROM_EMAIL_ADDRESS);
                var toAddress = new MailAddress(toEmail);
                const string fromPassword = FROM_PASSWORD;


                System.Net.Mail.SmtpClient smtp = new System.Net.Mail.SmtpClient
                {
                    Host = "smtp.gmail.com",
                    Port = 587,
                    EnableSsl = true,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(fromAddress.Address, fromPassword)
                };

                using (var message = new MailMessage(fromAddress, toAddress)
                {
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                })
                {
                    smtp.Send(message);
                }
            }
            catch
            {
            }
        }

        //function which is called from Autodesk exchange server when a user downloads a App
        public void IPNListener()
        {
            try
            {
                string ipnNotification = Encoding.ASCII.GetString(Request.BinaryRead(Request.ContentLength));
                if (ipnNotification.Trim() == string.Empty)
                {
                    return;
                }
                string validationMessage = HandleIPNNotification(ipnNotification);
            }
            catch
            {
            }

        }

        // refer to following link for the complete parameters
        // here is IPN API link: https://developer.paypal.com/webapps/developer/docs/classic/ipn/integration-guide/IPNandPDTVariables/
        /*

        A sample priced app/web service IPN sent to publisher:
             
        transaction_subject=200703030249937
        &txn_type=web_accept
        &payment_date=23%3A36%3A36+Jan+11%2C+2014+PST
        &last_name=name
        &residence_country=AU
        &item_name=appname
        &payment_gross=5.50
        &mc_currency=USD
        &business=paypal@company.com
        &payment_type=instant
        &protection_eligibility=Ineligible
        &payer_status=verified
        &verify_sign=AFcWxV21C7fd0v3bYYYRCpSSRl31AsmAEVMnS38537K1tk5tZMnvtnW6
        &tax=0.50
        &payer_email=buyer@company.net
        &txn_id=0AG18756HD086633A
        &quantity=1
        &receiver_email=paypal@company.com
        &first_name=name
        &payer_id=NEH6BJPL9LBYE
        &receiver_id=GDGRD3PAZBMD7
        &item_number=appstore.exchange.autodesk.com%appid%3Aen
        &handling_amount=0.00
        &payment_status=Completed
        &payment_fee=0.43
        &mc_fee=0.43
        &shipping=0.00
        &mc_gross=5.50
        &custom=200703030249937
        &charset=windows-1252
        &notify_version=3.7
        &auth=A6P4OiUSwAL6901WUc3VK.fiUaYTR5AND5h.XpBaMqrI8gSmid.n0tFsfAMP6u3unDXUuiABwGtZWQlN.RFtDcA
        &form_charset=UTF-8
        &buyer_adsk_account=xxx@company.com.au   //this one is added by exchange store for free paid IPN sent to publisher:
        */
        private string HandleIPNNotification(string notification)
        {
            string strResponse = "Unknown";

            try
            {
                //The value is the appid which is bought by customer
                string thisapp = HttpUtility.UrlDecode(Request["item_number"]);

                //custom logic to identify if you have publihsed this app or not...
                // POST IPN notification back to Autodesk Exchange to validate
                // whether it is a validate ipn notification from Autodesk Exchange
               
                bool verified = VerifyIpnValidation(notification, thisapp);


                //If the IPN notification is valid one, then it was sent from Autodesk Exchange Store, 
                if (verified)
                {
                    //get the txn_type..
                    //txn_type could be 
                    //web_accept - one time payment
                    //subscr_signup - Subscription started
                    //subscr_payment - Subscription payment received (new or renew)
                    //subscr_cancel - sbuscrition cacel

                    string txn_type = Request["txn_type"];

                    if (txn_type == "subscr_signup")
                    {
                        ProcessSubscriptionSignup(notification);
                    }
                    else if(txn_type == "subscr_payment")
                    {
                        ProcessSubscriptionPayment(notification);
                    }
                    else if (txn_type == "subscr_cancel")
                    {
                        ProcessSubscriptioncancel(notification);
                    }
                    else if (txn_type == "web_accept")
                    {
                        ProcessOneTimePayment(notification);
                    }
                    else
                    {
                        //Free App?
                        ProcessFreeApp(notification);
                    }
                }

            }
            catch
            {
            }

            return strResponse;
        }

        //for Free App, we can just read email id of the user downloanding and the App id.
        private void ProcessFreeApp(string notification)
        {
            string buyerEmail = Request["buyer_adsk_account"];
            string appId = HttpUtility.UrlDecode(Request["item_number"]);

            if (String.IsNullOrEmpty(appId) || String.IsNullOrEmpty(buyerEmail))
            {
                //error
            }
            else
            {
                AddPerpetualUser(buyerEmail, appId, "");
            }

            
        }

        //No action for now 
        private void ProcessSubscriptionSignup(string notification)
        {
            //No work when user is signing up...

        }

        //function which adds a new user or renews the Subscription
        private void ProcessSubscriptionPayment(string notification)
        {
            //this could be new or renew...get the user email id
            string buyerEmail = Request["buyer_adsk_account"];
            //get the app id from IPN notification
            string appId = HttpUtility.UrlDecode(Request["item_number"]);
            //get the app name from IPN notification
            string appName = HttpUtility.UrlDecode(Request["item_name"]);

            //add or update the user record.
            AddOrUpdateuserRecord(buyerEmail, appId, appName);

        }
        //function which cancels the Subscription
        private void ProcessSubscriptioncancel(string notification)
        {
            //this could be new or renew...get the user email id
            string buyerEmail = Request["buyer_adsk_account"];
            //get the app id from IPN notification
            string appId = HttpUtility.UrlDecode(Request["item_number"]);
            //get the app name from IPN notification
            string appName = HttpUtility.UrlDecode(Request["item_name"]);

            //add or update the user record.
            CancelUserSubscription(buyerEmail, appId, appName);
        }

        //function to handle the Perpetual licensing App
        private void ProcessOneTimePayment(string notification)
        {
            //one time payment....no time and date
            //this could be new or renew...get the user email id
            string buyerEmail = Request["buyer_adsk_account"];
            //get the app id from IPN notification
            string appId = HttpUtility.UrlDecode(Request["item_number"]);
            //get the app name from IPN notification
            string appName = HttpUtility.UrlDecode(Request["item_name"]);

            //.
            AddPerpetualUser(buyerEmail, appId, appName);
        }

        //function add a entry in licensing table for Perpetual license
        private bool AddPerpetualUser(string buyerEmail, string appId, string appName)
        {

            bool bRetun = false;
            // Validation
            if (String.IsNullOrEmpty(appId) || String.IsNullOrEmpty(buyerEmail))
            {
                return bRetun;
            }

            Connect2TableStorage();


            bool hasRow = _dataModelFactory.GetRecordEmailId(appId, buyerEmail);

            if (hasRow)
            {
                // Already registered, somthing wrong
                return bRetun;
            }
            else
            {
                //add new record...
                RegisterPerpetualUser(appId, appName, buyerEmail);

            }

            return true;
        }

        //functions which adds a new record in the licensing table
        private bool AddOrUpdateuserRecord(string buyerEmail, string appId, string appName)
        {
            bool bRetun = false;
            // Validation
            if (String.IsNullOrEmpty(appId) || String.IsNullOrEmpty(buyerEmail) || String.IsNullOrEmpty(appName))
            {
                return bRetun;
            }

            Connect2TableStorage();


            bool hasRow = _dataModelFactory.GetRecordEmailId(appId, buyerEmail);

            if (hasRow)
            {
                string sExpiryDateTimeStamp = "";
                // Already registered, check for expiry and extend the expiry date.
                if (_dataModelFactory.extendExpiryDate(appId, buyerEmail, ref sExpiryDateTimeStamp))
                {
                    //send a email with new sExpiryDateTimeStamp.
                    string bodyTemplate = @"Hi {0},<br/><br/>Thank you for renewing the subscribing to App! <br/><br/> Now, your subscription will end by {1} <br/><br/><br/>Thanks,<br/>Autodesk App Team.";
                    string body = string.Format(bodyTemplate, "", sExpiryDateTimeStamp);
                    string subject = "Autodesk Exchange store: Subscription renewed for App";
                    SendEmail(buyerEmail, subject, body);
                }

              
            }
            else
            {
                //add new record...
                bRetun = RegisterNewUser(appId, appName, buyerEmail);
            }

            return bRetun;
        }

        //function which cancel user subscription
        private bool CancelUserSubscription(string buyerEmail, string appId, string appName)
        {
            bool bRetun = false;
            // Validation
            if (String.IsNullOrEmpty(appId) || String.IsNullOrEmpty(buyerEmail) || String.IsNullOrEmpty(appName))
            {
                return bRetun;
            }

            Connect2TableStorage();


            bool hasRow = _dataModelFactory.GetRecordEmailId(appId, buyerEmail);

            if (hasRow)
            {
                // Already registered, check for expiry and extend the expiry date.
                if (_dataModelFactory.cancelSubscription(appId, buyerEmail))
                {
                    //send a email with new sExpiryDateTimeStamp.
                    string bodyTemplate = @"Hi {0},<br/><br/>As requested your subscription to App has been cancelled <br/><br/><br/>Thanks,<br/>Autodesk App Team.";
                    string body = string.Format(bodyTemplate, "");
                    string subject = "Autodesk Exchange store: Subscription cancelled for App";
                    SendEmail(buyerEmail, subject, body);
                }

            }
            else
            {
                return bRetun;

            }

            return true;
        }

        bool RegisterPerpetualUser(string appId, string appName, string useremail)
        {
            bool bRetun = false;
            // Validation
            if (String.IsNullOrEmpty(appId) || String.IsNullOrEmpty(useremail))
            {
                return bRetun;
            }

            Connect2TableStorage();

            //generate activation id.
            Guid activationId = Guid.NewGuid();
            string sActivationId = activationId.ToString("N");

            bRetun = _dataModelFactory.AddNewUser(appId, appName, useremail, sActivationId, true);

            if (bRetun)
            {
                //send a email with activation id.
                string bodyTemplate = @"Hi {0},<br/><br/>Thank you for purchasing App! <br/><br/> Please provide below activation code when prompted during first use of the FacetCurve App.<br/><br/>Activation code: {1} <br/><br/><br/>Thanks,<br/>Autodesk App Team.";
                string body = string.Format(bodyTemplate, "", sActivationId);
                string subject = "Autodesk Exchange store: Activation code for App";
                
                SendEmail(useremail, subject, body);
                
            }

            return bRetun;
        }

        bool RegisterNewUser(string appId, string appName, string useremail)
        {
            bool bRetun = false;
            // Validation
            if (String.IsNullOrEmpty(appId) || String.IsNullOrEmpty(useremail) || String.IsNullOrEmpty(appName))
            {
                return bRetun;
            }

            Connect2TableStorage();

            //generate activation id.
            Guid activationId = Guid.NewGuid();
            string sActivationId = activationId.ToString("N");

            bRetun = _dataModelFactory.AddNewUser(appId, appName, useremail, sActivationId, false);

            if (bRetun)
            {
                //send a email with activation id.
                string bodyTemplate = @"Hi {0},<br/><br/>Thank you for subscribing to App! <br/><br/> Please provide below activation code when prompted during first use of the FacetCurve App.<br/><br/>Activation Id: {1} <br/><br/><br/>Thanks,<br/>Autodesk App Team.";
                string body = string.Format(bodyTemplate, "", sActivationId);
                string subject = "Autodesk Exchange store: Activation code for App";
                SendEmail(useremail, subject, body);
            }

            return bRetun;
        }

        private bool VerifyIpnValidation(string notification, string thisapp)
        {
            string strResponse;

            // POST IPN notification back to Autodesk Exchange to validate
            //https://developer.paypal.com/webapps/developer/docs/classic/ipn/ht_ipn/
            //For Autodesk Exchange Store, you do not need to contact with Paypal directly, valide with Autodesk Exchange
            var postUrl = GetIpnValidationUrl();
            
            strResponse = GetResponseString(postUrl, notification);

            if (strResponse == "Verified")
            {
                return true;
            }
            else
            {
                // for testing, return true..
                //return true;
                return false;

            }
        }

        protected string GetIpnValidationUrl()
        {

            //Default to empty string
            string validationUrl = Convert.ToString(String.IsNullOrEmpty(ConfigurationManager.AppSettings["validationUrl"])
                                         ? "" : ConfigurationManager.AppSettings["validationUrl"]);

            if (string.IsNullOrWhiteSpace(validationUrl))
            {
                //Autodesk Exchange Store IPN validation URL
                return @"https://apps.exchange.autodesk.com/WebServices/ValidateIPN";
            }
            return validationUrl;

        }

        private string GetResponseString(string url, string poststring)
        {
            HttpWebRequest httpRequest =
            (HttpWebRequest)WebRequest.Create(url);

            httpRequest.Method = "POST";
            httpRequest.ContentType = "application/x-www-form-urlencoded";

            byte[] bytedata = Encoding.UTF8.GetBytes(poststring);
            httpRequest.ContentLength = bytedata.Length;

            Stream requestStream = httpRequest.GetRequestStream();
            requestStream.Write(bytedata, 0, bytedata.Length);
            requestStream.Close();


            HttpWebResponse httpWebResponse =
            (HttpWebResponse)httpRequest.GetResponse();
            Stream responseStream = httpWebResponse.GetResponseStream();

            StringBuilder sb = new StringBuilder();

            using (StreamReader reader =
            new StreamReader(responseStream, System.Text.Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    sb.Append(line);
                }
            }

            return sb.ToString();

        }

        private void Connect2TableStorage()
        {
            if (_dataModelFactory == null)
            {
                // create data factory to table
                _dataModelFactory = new DBModelFactory(cloudStorageAccount.CreateCloudTableClient());
            }
        }

      
    }
}
