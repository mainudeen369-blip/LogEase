"use client";

import { useRouter } from "next/navigation";
import { useEffect } from "react";
import { getToken } from "../lib/api";

export default function HomePage() {
  const router = useRouter();
  useEffect(() => {
    router.replace(getToken() ? "/compose" : "/login");
  }, [router]);
  return <p style={{ padding: "2rem" }}>Loading…</p>;
}
