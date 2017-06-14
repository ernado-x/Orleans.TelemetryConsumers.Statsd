# OrleansStatsDUtils

Orleans statistics provider for StatsD

## Supports
- [x] Silo statistics
- [x] Client statistics
- [ ] .NET Core

## Installation
Using the Package Manager Console:

```
PM> Install-Package SBTech.OrleansStatsDUtils 
```

## Configuration for Silo
```xml
<?xml version="1.0" encoding="utf-8" ?>
<OrleansConfiguration xmlns="urn:orleans">
  <Globals>
    <StatisticsProviders>
      <Provider Type="SBTech.OrleansStatsDUtils.StatsDStatisticsProvider"
                Name="StatsDStatisticsProvider"
                StatsDServerName="localhost"
                StatsDServerPort="8125"
                StatsDPrefix="app_name"
                StatsDMaxUdpPacketSize="512"/>
    </StatisticsProviders>
  </Globals>  
</OrleansConfiguration>
```
