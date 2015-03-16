using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Text;
using Microsoft.WindowsAzure;
using System.Data.Services.Client;
using Microsoft.WindowsAzure.ServiceRuntime;
using System.Diagnostics;
using Microsoft.WindowsAzure.Storage.Table.DataServices;
using Microsoft.WindowsAzure.Storage.Table;
using System.Globalization;

namespace MvcWebRole1
{
    public class DBModel : TableEntity
    {
        // Unique application Id and AppName
        public String AppId { get; set; }

        //name
        public String AppName { get; set; }

        //activation id
        public String ActivationId { get; set; }

        // MachineLockCode 
        public String MachineLockCode { get; set; }

        public String StartDateTimeStamp { get; set; }

        public String LastRunDateTimeStamp { get; set; }

        public String ExpiryDateTimeStamp { get; set; }

        //user email id
        public String EmailId { get; set; }

        // Constructor Required and used by Table classes
        public DBModel()
            : base()
        {
            this.PartitionKey = string.Empty;
            this.RowKey = Guid.NewGuid().ToString();

            Initialize();
        }


        // Constructor passing in rowkey and partitionkey
        public DBModel(string partitionKey, string rowKey)
            : base(partitionKey, rowKey)
        {
            Initialize();
        }

        public void Initialize()
        {
            String _timeFormat = "dd-MM-yyyy hh:mm:ss tt";
            MachineLockCode = String.Empty;

            // Initial values
            DateTime currentDateTimeStamp = DateTime.Now;
            String sCurrentTimeStamp = currentDateTimeStamp.ToString(_timeFormat, CultureInfo.InvariantCulture);
            StartDateTimeStamp = sCurrentTimeStamp;
            LastRunDateTimeStamp = sCurrentTimeStamp;
            ExpiryDateTimeStamp = sCurrentTimeStamp;

            // Contact Details
            EmailId = String.Empty;
        }
    }

    public class DBModelFactory
    {
        // Azure Storage Table Name where the licensing and usage statistics details are stored.
        private readonly string AzureTableName = "LicenseMasterTable";

        // Azure Storage Table Access object
        private readonly CloudTableClient cloudTableClient;

        String _timeFormat = "dd-MM-yyyy hh:mm:ss tt";

        /// <summary>
        /// C'tor
        /// </summary>
        /// <param name="eventCloudStorageAccount">Event Storage Account</param>
        public DBModelFactory(CloudTableClient cloudTableClient)
        {
            this.cloudTableClient = cloudTableClient;
            CloudTable table = this.cloudTableClient.GetTableReference(AzureTableName);
            // Table Holding the data
            table.CreateIfNotExists();
            
        }

        /// <summary>
        /// Create a New Object - doesn't put it in storage
        /// </summary>
        /// <returns></returns>
        public DBModel Create(string appId, string appName, string email, string activationId, string expiryDateTimeStamp, string lastRunDateTimeStamp)
        {
            DBModel entity = new DBModel()
            {
                AppId = appId,
                AppName = appName,
                MachineLockCode = "",
                StartDateTimeStamp = lastRunDateTimeStamp,
                ExpiryDateTimeStamp = expiryDateTimeStamp,
                LastRunDateTimeStamp = lastRunDateTimeStamp,
                ActivationId = activationId,
                EmailId = email
            };

            return (entity);
        }

        /// <summary>
        /// Add Object to storage
        /// </summary>
        public void Add(DBModel entity)
        {
            try
            {
                CloudTable table = cloudTableClient.GetTableReference(AzureTableName);
                TableOperation insertOperation = TableOperation.Insert(entity);
                table.Execute(insertOperation);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("*****AzureDataModelFactory:Add ex.Message=" + ex.Message);
            }
        }

        /// <summary>
        /// Fetch Most Recent Singleton
        /// </summary>
        /// <returns></returns>
        public DBModel FetchNewest()
        {
            return FetchAll().FirstOrDefault();
        }

        /// <summary>
        /// Fetch Top N objects
        /// </summary>
        /// <returns></returns>
        public IEnumerable<DBModel> FetchTopN(int count)
        {
            if (count > 0)
            {
                return FetchAll().Take(count);
            }
            else if (count == -1) // -1 implies all wanted
            {
                return FetchAll();
            }
            else // count == 0 implies none wanted 
            {
                return null;
            }
        }

        /// <summary>
        /// Fetch All Entities (date desc because that is how they were entered)
        /// </summary>
        /// <returns></returns>
        public IEnumerable<DBModel> FetchAll()
        {
            CloudTable table = cloudTableClient.GetTableReference(AzureTableName);
            return table.CreateQuery<DBModel>().Execute();

        }
        /// <summary>
        /// Count all items in storage 
        /// </summary>
        /// <returns></returns>
        public int CountAll()
        {
            return FetchAll().Count();
        }

        /// <summary>
        /// get the record for given App id and machine
        /// </summary>
        /// <returns></returns>
        private IEnumerable<DBModel> FetchByAppIdandMachineLockCode(string appId, string machineLockCode)
        {
            CloudTable table = cloudTableClient.GetTableReference(AzureTableName);


            var query = from entity in table.CreateQuery<DBModel>().Execute()
                        where (entity.AppId == appId && entity.MachineLockCode == machineLockCode)
                        select entity;

            return query;
        }

        /// <summary>
        /// get the record for given App id and email id
        /// </summary>
        /// <returns></returns>
        private IEnumerable<DBModel> FetchByAppIdandEmailId(string appId, string emailid)
        {
            CloudTable table = cloudTableClient.GetTableReference(AzureTableName);


            var query = from entity in table.CreateQuery<DBModel>().Execute()
                        where (entity.AppId == appId && entity.EmailId == emailid)
                        select entity;

            return query;

        }

        /// <summary>
        /// get the record for given App id and action id
        /// </summary>
        /// <returns></returns>
        private IEnumerable<DBModel> FetchByAppIdandActivationId(string appId, string activationId)
        {
            CloudTable table = cloudTableClient.GetTableReference(AzureTableName);


            var query = from entity in table.CreateQuery<DBModel>().Execute()
                        where (entity.AppId == appId && entity.ActivationId == activationId)
                        select entity;

            return query;
        }

        // Add a new record to the table with the values passed in as parameters
        public bool AddRecord(string appId, string appName, string email, string activationId, string expiryDateTimeStamp, string lastRunDateTimeStamp)
        {
            bool status = false;
            try
            {
                Add(Create(appId, appName, email, activationId, expiryDateTimeStamp, lastRunDateTimeStamp));
                status = true;
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine("*****AzureDataModelFactory:AddRecord ex.Message=" + ex.Message);
            }
            return status;
        }

        public bool GetRecordEmailId(string appId, string emaildId)
        {
            bool status = false;
            try
            {
                IEnumerable<DBModel> datamodels = FetchByAppIdandEmailId(appId, emaildId);
                foreach (DBModel dm in datamodels)
                {
                    status = true;
                    break;
                }
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine("*****AzureDataModelFactory:GetRecord ex.Message=" + ex.Message);
            }
            return status;
        }


        // Finds a record in the table with the values passed in as parameters
        public bool GetRecordActivationId(string appId, string activationId)
        {
            bool status = false;
            try
            {
                IEnumerable<DBModel> datamodels = FetchByAppIdandMachineLockCode(appId, activationId);
                foreach (DBModel dm in datamodels)
                {
                    status = true;
                    break;
                }
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine("*****AzureDataModelFactory:GetRecord ex.Message=" + ex.Message);
            }
            return status;
        }

        //function which cancels Subscription
        public bool cancelSubscription(string appId, string emaildId)
        {
            bool status = false;

            try
            {
                IEnumerable<DBModel> datamodels = FetchByAppIdandEmailId(appId, emaildId);

                //at present one user one record...
                if (datamodels.Count() > 1)
                {
                    return status;
                }

                DBModel dm = datamodels.ElementAt(0);

                //extend the 
                DateTime dbExpiryTimeStamp = DateTime.Now;
                DateTime.TryParseExact(dm.ExpiryDateTimeStamp, _timeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out dbExpiryTimeStamp);


                //still has valid time, so extend from valid time
                if (dbExpiryTimeStamp > DateTime.Now)
                {
                    dm.ExpiryDateTimeStamp = DateTime.Now.ToString(_timeFormat, CultureInfo.InvariantCulture);
                    //update records
                    CloudTable table = cloudTableClient.GetTableReference(AzureTableName);
                    TableOperation replaceOperation = TableOperation.Replace(dm);
                    table.Execute(replaceOperation);
                    status = true;
                }
               
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine("*****AzureDataModelFactory:GetRecord ex.Message=" + ex.Message);
            }
            return status;
        }

        //function which extends Subscription
        public bool extendExpiryDate(string appId, string emaildId, ref string sExpiryDateTimeStamp)
        {
            bool status = false;

            try
            {
                IEnumerable<DBModel> datamodels = FetchByAppIdandEmailId(appId, emaildId);

                //at present one user one record...
                if (datamodels.Count() > 1)
                {
                    return status;
                }

                DBModel dm = datamodels.ElementAt(0);

                //extend the 
                DateTime dbExpiryTimeStamp = DateTime.Now;
                DateTime.TryParseExact(dm.ExpiryDateTimeStamp, _timeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None,out dbExpiryTimeStamp);

               
                //still has valid time, so extend from valid time
               if(dbExpiryTimeStamp > DateTime.Now)
               {
                   DateTime newExpiryDate = dbExpiryTimeStamp.AddDays(30.0);
                   dm.ExpiryDateTimeStamp = newExpiryDate.ToString(_timeFormat, CultureInfo.InvariantCulture);
                   
               }
               else
               {
                   //extend from current time
                   DateTime newExpiryDate = DateTime.Now.AddDays(30.0);
                   dm.ExpiryDateTimeStamp = newExpiryDate.ToString(_timeFormat, CultureInfo.InvariantCulture);
               }

               sExpiryDateTimeStamp = dm.ExpiryDateTimeStamp;

                //update records
               CloudTable table = cloudTableClient.GetTableReference(AzureTableName);
               TableOperation replaceOperation = TableOperation.Replace(dm);
               table.Execute(replaceOperation);
               status = true;

            }
            catch (System.Exception ex)
            {
                Debug.WriteLine("*****AzureDataModelFactory:GetRecord ex.Message=" + ex.Message);
            }
            return status;
        }

        //functions adds a new user...
        public bool AddNewUser(string appId, string appName, string email, string activationId, bool Perpetual)
        {
            bool status = false;
            try
            {
                //
                DateTime currentDateTimeStamp = DateTime.Now;
                DateTime expiryDateTimeStamp = DateTime.Now.AddDays(30);

                if (Perpetual)
                {
                    //add 100 years :) :)
                    expiryDateTimeStamp = DateTime.Now.AddYears(100);
                }

                String sExpiryTimeStamp = expiryDateTimeStamp.ToString(_timeFormat, CultureInfo.InvariantCulture);
                String sCurrentTimeStamp = currentDateTimeStamp.ToString(_timeFormat, CultureInfo.InvariantCulture);

                Add(Create(appId, appName, email, activationId, sExpiryTimeStamp, sCurrentTimeStamp));
                status = true;
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine("*****AzureDataModelFactory:AddRecord ex.Message=" + ex.Message);
            }
            return status;
        }

        //function activates given App in the machine
        public int activateApp(string activationId, string appId, string machineCode, ref string sExpiryDateTimeStamp, ref string buyerEmail)
        {
            int status = 1;

            try
            {
                IEnumerable<DBModel> datamodels = FetchByAppIdandActivationId(appId, activationId);

                int nCount = datamodels.Count();

                if (nCount == 0)
                {
                    //no recaord...
                    return 3;
                }

                //at present one user one record...
                if (nCount > 1)
                {
                    return 1;
                }

                DBModel dm = datamodels.ElementAt(0);


                if (String.IsNullOrEmpty(dm.MachineLockCode) == false)
                {
                    if (string.Compare(dm.MachineLockCode, machineCode, true) != 0)
                    {
                        return 2; //wrong machine id
                    }
                }

                //update the machine id.
                dm.MachineLockCode = machineCode;

                
                //update last use date and time.
                String sLastRunDateTimeStamp = DateTime.Now.ToString(_timeFormat, CultureInfo.InvariantCulture);
                dm.LastRunDateTimeStamp = sLastRunDateTimeStamp;
                sExpiryDateTimeStamp = dm.ExpiryDateTimeStamp;
                buyerEmail = dm.EmailId;

                //update records
                CloudTable table = cloudTableClient.GetTableReference(AzureTableName);
                TableOperation replaceOperation = TableOperation.Replace(dm);
                table.Execute(replaceOperation);

                //scucess
                status = 0;

            }
            catch (System.Exception ex)
            {
                Debug.WriteLine("*****AzureDataModelFactory:GetRecord ex.Message=" + ex.Message);
            }
            return status;
        }

        //function checks Entitlement, provided activationId, appId, machinecode
        public int CheckAppEntitlement(string activationId, string appId, string machineCode, ref string expiryDateTimeStamp)
        {
            int status = 3;

            try
            {
                IEnumerable<DBModel> datamodels = FetchByAppIdandActivationId(appId, activationId);

                int nCount = datamodels.Count();

                if (nCount == 0)
                {
                    return 4;
                }

                //at present one user one record...
                if (nCount > 1)
                {
                    return 1;
                }

                DBModel dm = datamodels.ElementAt(0);

                if (string.Compare(dm.MachineLockCode, machineCode, true) != 0)
                {
                    return 2; //wrong machine id
                }

                DateTime dbExpiryTimeStamp = DateTime.Now;
                DateTime.TryParseExact(dm.ExpiryDateTimeStamp, _timeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out dbExpiryTimeStamp);

                //current date is less then expiry
                if (dbExpiryTimeStamp < DateTime.Now)
                {
                    return 3; //expired
                }

                //update expiryDateTimeStamp
                expiryDateTimeStamp = dm.ExpiryDateTimeStamp;

                //update last use date and time.
                String sLastRunDateTimeStamp = DateTime.Now.ToString(_timeFormat, CultureInfo.InvariantCulture);
                dm.LastRunDateTimeStamp = sLastRunDateTimeStamp;

                //update records
                CloudTable table = cloudTableClient.GetTableReference(AzureTableName);
                TableOperation replaceOperation = TableOperation.Replace(dm);
                table.Execute(replaceOperation);

                status = 0;
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine("*****AzureDataModelFactory:GetRecord ex.Message=" + ex.Message);
            }
            return status;
        }


        public bool deleteAll()
        {
            bool status = false;
            try
            {
                // Create storage context
                CloudTable table = cloudTableClient.GetTableReference(AzureTableName);
                IEnumerable<DBModel> datamodels = FetchAll();
                foreach (DBModel dm in datamodels)
                {
                    TableOperation delete = TableOperation.Delete(dm);
                    table.Execute(delete);
                    status = true;
                }
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine("*****AzureDataModelFactory:UpdateRecord2 ex.Message=" + ex.Message);
            }
            return status;
        }

    }
}