# A Useful Custom Function to Debug Firestore Security Rules

Firestore security rules provide a function [`debug`](https://firebase.google.com/docs/reference/rules/rules.debug#debug), which logs the given value to `firestore-debug.log` (only when using the Firestore Emulator; it's no-op in production). But it just prints the value, with no information on its context. When your security rule doesn't work as expected, you might wrap every suspicious expression with `debug` and then struggle to figure out the correspondence between each log entires and the plenty calls to `debug`.

What if there's a function to log custom messages that explain why the request is denied? Like:

```js

allow get: if
  // logs "not admin" if the user's role is not admin
  assert(request.auth.role == "admin", "not admin") &&
  // logs "email is not verified" if email_verified is false
  assert(request.auth.email_verified, "email is not verified");
```

Actually, you can implement this `assert` function! The definition is:

```js
function assert(condition, message) {
  return condition || debug(message) && false;
}
```

When `condition` is truthy, it just returns `condition`. Otherwise, it logs `message` to `firestore-debug.log` and returns `false`.

I hope this function helps you debug your security rules!
