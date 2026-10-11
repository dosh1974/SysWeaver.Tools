using System;
using System.Threading.Tasks;
using SysWeaver;
using SysWeaver.Security;

namespace AcmeCertUtil
{
    internal class AcmeCertUtilProgram
    {
        //  Process exit codes, 0 = success, non-zero (1-255) = failure.
        //  0-2 are the same as SysWeaver.CommandLine FilesToFolderTool.ExitSuccess / ExitGenericError / ExitInvalidArguments.

        /// <summary>
        /// Exit code: success (also returned when help was requested).
        /// </summary>
        const int ExitSuccess = 0;
        /// <summary>
        /// Exit code: generic error, failed to get the certificate.
        /// </summary>
        const int ExitGenericError = 1;
        /// <summary>
        /// Exit code: invalid command line (wrong number of arguments).
        /// </summary>
        const int ExitInvalidArguments = 2;

        static bool IsHelp(String arg) => arg is "-?" or "/?" or "-h" or "-help" or "--help" or "/help";

        /// <summary>
        /// Parse the optional renew argument
        /// </summary>
        /// <param name="arg">The argument</param>
        /// <param name="renew">True if the certificate should be renewed</param>
        /// <returns>True if the argument is valid</returns>
        static bool TryParseRenew(String arg, out bool renew)
        {
            switch (arg.ToLowerInvariant())
            {
                case "renew":
                case "true":
                case "yes":
                case "1":
                    renew = true;
                    return true;
                case "norenew":
                case "false":
                case "no":
                case "0":
                    renew = false;
                    return true;
            }
            renew = false;
            return false;
        }

        static int Usage(String err, int retCode)
        {
            if (err != null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(err);
                Console.ResetColor();
            }
            Console.WriteLine("Use: AcmeCertUtil.exe  <email> <domainName> [password] [renew] [filename.pfx]");
            Console.WriteLine("Default password = email");
            Console.WriteLine("renew = \"renew\" (or true, yes, 1) to delete the existing certificate file and request a new certificate, \"norenew\" (or false, no, 0) to use the existing one if it's valid (default)");
            Console.WriteLine("Default filename = domainName.pfx");
            Console.WriteLine("Exit codes: 0 = success or help, 1 = failed to get the certificate, 2 = invalid command line");
            return retCode;
        }

        static async Task<int> Main(string[] args)
        {
            var al = args.Length;
            if ((al < 2) || (al > 5))
                return ((al == 1) && IsHelp(args[0])) ? Usage(null, ExitSuccess) : Usage(null, ExitInvalidArguments);
            if ((al > 3) && (!TryParseRenew(args[3], out var _)))
                return Usage("Invalid renew argument \"" + args[3] + "\"!", ExitInvalidArguments);
            try
            {
                return await Run(args);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex.Message);
                Console.ResetColor();
                return ExitGenericError;
            }
        }

        static async Task<int> Run(string[] args)
        {
            var al = args.Length;
            String email = args[0];
            String domainName = args[1];
            String password = al > 2 ? args[2] : email;
            bool forceRenew = false;
            if (al > 3)
                TryParseRenew(args[3], out forceRenew);
            var destName = al > 4 ? args[4] : (domainName + ".pfx");
            var mh = new MessageHost();
            mh.AddMessageHandler(ConsoleMessageHandler.GetSync());

            var acme = new AcmeCertificateProvider(mh, new AcmeCertificateParams
            {
                DomainName = domainName,
                Email = email,
                Password = password,
                Filename = destName,
            });
            var fn = acme.Filename;
            if (forceRenew)
                await PathExt.TryDeleteFileAsync(fn);
            var cert = await acme.GetCert();
            mh.AddMessage("Certificate found at " + fn.ToQuoted());
            mh.Flush();
            return ExitSuccess;
        }
    }
}
