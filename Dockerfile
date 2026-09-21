FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore LidarrLens.sln
RUN dotnet publish src/LidarrLens.Web/LidarrLens.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV DATA_DIRECTORY=/data
EXPOSE 8080
VOLUME ["/data"]
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "LidarrLens.Web.dll"]
