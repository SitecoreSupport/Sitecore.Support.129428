using ISearchProvider = Sitecore.Social.Search.ISearchProvider;

namespace Sitecore.Support.Social.IoC.Modules
{
    public class SupportModule : Sitecore.Social.IoC.Modules.ProvidersModule
    {
        public override void Load()
        {
            this.Rebind<ISearchProvider>().To<Sitecore.Support.Social.Search.SearchProvider>();
        }
    }   
}