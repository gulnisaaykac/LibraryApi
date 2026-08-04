# Microsoft SDK imaji - derleme asaması
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Once sadece csproj (cache icin)
COPY Library.DataAccess/Library.DataAccess.csproj Library.DataAccess/
COPY LibraryApi/LibraryApi.csproj LibraryApi/
RUN dotnet restore LibraryApi/LibraryApi.csproj

# Kaynak kodu kopyala ve publish et
COPY Library.DataAccess/ Library.DataAccess/
COPY LibraryApi/ LibraryApi/
RUN dotnet publish LibraryApi/LibraryApi.csproj -c Release -o /app/publish

# Runtime asaması (daha kucuk imaj)
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "LibraryApi.dll"]