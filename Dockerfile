# Build stage: restore and publish the web app.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first so the restore layer is cached until dependencies change.
COPY Quotebook.sln ./
COPY src/Quotebook.Core/Quotebook.Core.csproj src/Quotebook.Core/
COPY src/Quotebook.Data/Quotebook.Data.csproj src/Quotebook.Data/
COPY src/Quotebook.Web/Quotebook.Web.csproj src/Quotebook.Web/
COPY tests/Quotebook.Tests/Quotebook.Tests.csproj tests/Quotebook.Tests/
RUN dotnet restore

COPY . .
RUN dotnet publish src/Quotebook.Web/Quotebook.Web.csproj -c Release -o /app --no-restore

# Run stage: the small ASP.NET runtime image, as the non-root user it ships with.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
USER $APP_UID

# Render sends traffic to port 10000 unless told otherwise.
ENV ASPNETCORE_HTTP_PORTS=10000
EXPOSE 10000
ENTRYPOINT ["dotnet", "Quotebook.Web.dll"]
