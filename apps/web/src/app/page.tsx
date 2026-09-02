import { redirect } from "next/navigation";

// The proxy guard sends signed-out visitors to /login before this runs.
export default function Index() {
  redirect("/home");
}
