using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using System;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;

namespace AzureFunction.VstsExtension.LaunchDarkly.AzureFunctions
{
    public static class UpdateUserFeatureFlag
    {

        private static string key = TelemetryConfiguration.Active.InstrumentationKey = System.Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY", EnvironmentVariableTarget.Process);
        private static TelemetryClient telemetry = new TelemetryClient() { InstrumentationKey = key };

        [FunctionName("UpdateUserFeatureFlag")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)]HttpRequestMessage req, ExecutionContext context, TraceWriter log)
        {
            try
            {
                telemetry.Context.Operation.Id = context.InvocationId.ToString();
                telemetry.Context.Operation.Name = "UpdateUserFeatureFlag";
                int apiversion = Helpers.GetHeaderValue(req, "api-version");


                var data = req.Content.ReadAsStringAsync().Result; //Gettings parameters in Body request
                log.Info(data);
                var startTime = DateTime.Now;
                var timer = System.Diagnostics.Stopwatch.StartNew();

                var formValues = data.Split('&')
                    .Select(value => value.Split('='))
                    .ToDictionary(pair => Uri.UnescapeDataString(pair[0]).Replace("+", " "),
                                  pair => Uri.UnescapeDataString(pair[1]).Replace("+", " "));
                
                string account = formValues["account"];
                string LDproject = formValues["ldproject"];
                string LDenv = formValues["ldenv"];
                string feature = formValues["feature"];
                string active = formValues["active"];
                string appSettingExtCert = (apiversion >= 3) ? formValues["appsettingextcert"]: string.Empty; //"RollUpBoard_ExtensionCertificate"

                string issuedToken = Helpers.GetUserTokenInRequest(req);

                //Check the token, and compare with the UserId
                string extcert = Helpers.GetExtCertificatEnvName(appSettingExtCert, apiversion, 3);
                var tokenuserId = CheckVSTSToken.checkTokenValidity(issuedToken, extcert);

                if (tokenuserId != null)
                {
                    var userkey = LaunchDarklyServices.FormatUserKey(tokenuserId, account);
                    HttpResponseMessage updateStatusResponse = await LaunchDarklyServices.UpdateUserFlag(LDproject, LDenv, userkey, feature, Convert.ToBoolean(active));

                    telemetry.TrackDependency("LaunchDarklyRestAPI", "UpdateUserFlag", startTime, timer.Elapsed, updateStatusResponse.Content != null);

                    if (updateStatusResponse.StatusCode == HttpStatusCode.NoContent)
                    {
                        telemetry.TrackEvent("The flag is updated");
                        return req.CreateResponse(HttpStatusCode.OK, "The flag is updated");
                    }
                    else
                    {
                        telemetry.TrackTrace("Error to update the flag with data: " + updateStatusResponse.Content);
                        return req.CreateResponse(HttpStatusCode.BadRequest, "Error to update the flag with data: " + updateStatusResponse.Content);
                    }
                }
                else
                {
                    telemetry.TrackTrace("The token is not valid");
                    return req.CreateResponse(HttpStatusCode.InternalServerError, "The token is not valid");
                }
            }
            catch (Exception ex)
            {
                telemetry.TrackException(ex);
                return req.CreateResponse(HttpStatusCode.InternalServerError, ex);
            }
        }
    }
}
