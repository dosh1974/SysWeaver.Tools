using System;
using System.Threading.Tasks;
using SysWeaver;
using SysWeaver.Security;

namespace AcmeCertUtil
{
    internal class AcmeCertUtilProgram
    {
        static async Task Main(string[] args)
        {
            var al = args.Length;
            if ((al < 2) || (al > 4))
            {
                Console.WriteLine("Use: AcmeCertUtil.exe  <email> <domainName> [password] [renew] [filename.pfx]");
                Console.WriteLine("Default password = email");
                Console.WriteLine("Default filename = domainName.pfx");
                return;
            }



            String email = args[0];
            String domainName = args[1];
            String password = al > 2 ? args[2] : email;
            bool forceRenew = al > 3;
            var destName = al > 4 ? args[3] : (domainName + ".pfx");
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
        }
    }
}
