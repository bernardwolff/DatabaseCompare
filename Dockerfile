FROM microsoft/dotnet:2.2-sdk AS build-env
COPY src /app/
WORKDIR /app
RUN dotnet publish -c Release

FROM mcr.microsoft.com/dotnet/core/runtime:2.2 as runtime
WORKDIR /app
COPY --from=build-env /app/bin/Release/netcoreapp2.2/publish/ ./

ENTRYPOINT ["dotnet", "DatabaseCompare.dll"]