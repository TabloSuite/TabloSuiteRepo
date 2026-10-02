# Instructions de mise à niveau .NET

## Contexte du projet

- Application Blazor Web App ciblant `net10.0`.
- La solution contient les projets `SkoulouApp.WebApp`, `SkoulouApp.Core` et `SkoulouApp.Tests`.
- La base de données utilise SQL Server avec Entity Framework Core.

## Procédure de mise à niveau

1. Mettre à jour les frameworks cibles et les références de packages de tous les projets concernés.
2. Restaurer les dépendances et générer la solution complète.
3. Corriger les erreurs de compilation et les avertissements liés à la mise à niveau sans contourner leur cause.
4. Vérifier les migrations EF Core avec :

   `dotnet ef migrations list --project SkoulouApp.WebApp/SkoulouApp.WebApp.csproj --startup-project SkoulouApp.WebApp/SkoulouApp.WebApp.csproj`

5. Appliquer les migrations en attente à la base de données de développement avec :

   `dotnet ef database update --project SkoulouApp.WebApp/SkoulouApp.WebApp.csproj --startup-project SkoulouApp.WebApp/SkoulouApp.WebApp.csproj`

6. Exécuter tous les tests du projet `SkoulouApp.Tests`.
7. Vérifier le démarrage de l'application Blazor et les parcours principaux, notamment l'authentification, les abonnements, la facturation et les finances.

## Règles de sécurité

- Ne jamais modifier ou supprimer une migration déjà appliquée.
- Ne jamais placer de secrets, chaînes de connexion de production ou clés d'API dans les fichiers suivis par Git.
- Avant tout déploiement, confirmer que les migrations en attente ont été examinées et approuvées.
- Les textes visibles par les utilisateurs doivent rester en français ; la désignation visible de `SuperAdmin` est toujours `Administrateur`.

## Validation finale

La mise à niveau est terminée uniquement lorsque la solution est compilée, les migrations sont à jour et tous les tests réussissent.