# Sitecore.Support.129428
Social SearchProvider can produce 'Index was not found' exceptions after recycles

## Description
After recycles, `Index was not found` exceptions can appear in Sitecore log files. The exceptions are raised during execution of the `SearchProvider` type:
```
1548 09:17:23 WARN  Sitecore shutting down
1548 09:17:23 WARN  Shutdown message: HostingEnvironment initiated shutdown
...
10096 09:17:34 ERROR There is no appropriate index for /sitecore/social/Messages - {DBB82699-FEE0-4A1E-9B52-FACC436925C6}. You have to add an index crawler that will cover this item
10096 09:17:34 ERROR Sitecore.Social: Social messages index could not be determined for master database. Messages root path: /sitecore/social/Messages. Please check indexes configuration.
Exception: System.ArgumentException
Message: Index  was not found
Source: Sitecore.ContentSearch
   at Sitecore.ContentSearch.ContentSearchManager.GetIndex(String name)
   at Sitecore.Social.Search.SearchProvider.GetSearchIndex()
```

The patch is designed to prevent the exceptions and log a warning if the `SearchProvider` tries to resolve the Social index after the indexes collection is cleared. 

## License  
This patch is licensed under the [Sitecore Corporation A/S License for GitHub](https://github.com/sitecoresupport/Sitecore.Support.129428/blob/master/LICENSE).  

## Download  
Downloads are available via [GitHub Releases](https://github.com/sitecoresupport/Sitecore.Support.129428/releases).  
