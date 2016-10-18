using ISearchProvider = Sitecore.Social.Search.ISearchProvider;
using Ninject.Modules;

namespace Sitecore.Support.Social.IoC.Modules
{
    public class SupportModule : NinjectModule
    {
        public override void Load()
        {
            this.Rebind<ISearchProvider>().To<Sitecore.Support.Social.Search.SearchProvider>();
        }
    }   
}