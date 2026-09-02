import { NextResponse, type NextRequest } from "next/server";

/// Cheap cookie-presence guard for redirect UX only — real session validity is
/// the api's business; the client's silent refresh settles it.
export default function proxy(request: NextRequest) {
  const hasSession = request.cookies.has("bandroom_refresh");
  const { pathname } = request.nextUrl;
  const isAuthPage = pathname === "/login" || pathname === "/register";

  if (!hasSession && !isAuthPage) {
    return NextResponse.redirect(new URL("/login", request.url));
  }

  if (hasSession && isAuthPage) {
    return NextResponse.redirect(new URL("/home", request.url));
  }

  return NextResponse.next();
}

export const config = {
  matcher: ["/", "/home", "/login", "/register"],
};
