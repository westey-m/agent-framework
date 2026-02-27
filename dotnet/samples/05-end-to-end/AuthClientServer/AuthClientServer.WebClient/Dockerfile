FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Copy solution-level files for restore
COPY Directory.Build.props Directory.Build.targets Directory.Packages.props global.json nuget.config ./
COPY eng/ eng/
COPY src/Shared/ src/Shared/
COPY samples/Directory.Build.props samples/

# Create sentinel file so $(RepoRoot) resolves correctly inside the container.
# RepoRoot is the parent of the dir containing CODE_OF_CONDUCT.md,
# and src projects import $(RepoRoot)/dotnet/nuget/nuget-package.props.
RUN touch /CODE_OF_CONDUCT.md

# Copy project file for restore
COPY samples/05-end-to-end/AuthClientServer/AuthClientServer.WebClient/AuthClientServer.WebClient.csproj samples/05-end-to-end/AuthClientServer/AuthClientServer.WebClient/

RUN dotnet restore samples/05-end-to-end/AuthClientServer/AuthClientServer.WebClient/AuthClientServer.WebClient.csproj -p:TargetFramework=net10.0 -p:TreatWarningsAsErrors=false

# Copy everything and build
COPY samples/05-end-to-end/AuthClientServer/AuthClientServer.WebClient/ samples/05-end-to-end/AuthClientServer/AuthClientServer.WebClient/
RUN dotnet publish samples/05-end-to-end/AuthClientServer/AuthClientServer.WebClient/AuthClientServer.WebClient.csproj -c Release -f net10.0 -o /app -p:TreatWarningsAsErrors=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "AuthClientServer.WebClient.dll"]
