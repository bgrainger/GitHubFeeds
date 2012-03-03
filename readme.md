# GitHubFeeds

## What Is It?

GitHubFeeds is an ASP.NET MVC project that returns ATOM feeds from data
exposed by the [GitHub API](http://developer.github.com/v3/).

## Why?

There are many data sources (like commit comments) that are only available
through the API. It's often useful to be able to consume these in an RSS
reader instead of having to visit the web.

This project was inspired by a feature that was in GitHub:FI (the previous
version of [GitHub Enterprise](https://enterprise.github.com/)).

## How Do I Use It?

Download the code, build it, and deploy it to a server that can host ASP.NET
MVC applications. Then, access:

http://_host_/comments/_server_/_user_/_repo_

where:

* _host_ is where you've deployed the application
* _server_ is the GitHub server; use `api.github.com` for github.com, or
  the hostname for GitHub Enterprise
* _user_ is the GitHub user, e.g., `bgrainger`
* _repo_ is the GitHub repo, e.g., `GitHubFeeds`

## What Feeds Are Supported?

The current version only exposes commit comments as a feed.

## What About Commits?

GitHub.com already provides an ATOM feed for the commits themselves; there's
an RSS link on the "Commits" page. For an example, access this repo's
[commit feed](https://github.com/bgrainger/GitHubFeeds/commits/master.atom).
