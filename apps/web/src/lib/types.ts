import type { components } from "@bandroom/client";

export type Schemas = components["schemas"];

export type BandSummary = Schemas["BandSummaryResponse"];
export type BandDetail = Schemas["BandDetailResponse"];
export type Band = Schemas["BandResponse"];
export type Member = Schemas["MemberResponse"];
export type WeeklySlot = Schemas["WeeklySlot"];
export type PracticeSlot = Schemas["PracticeSlot"];
export type IsoDay = Exclude<Schemas["IsoDayOfWeek"], "none">;
export type MyAvailability = Schemas["MyAvailabilityResponse"];
export type BandAvailability = Schemas["BandAvailabilityResponse"];
export type MemberAvailability = Schemas["MemberAvailabilityResponse"];
export type BlockoutException = Schemas["ExceptionResponse"];
export type FinderResult = Schemas["PracticeFinderResponse"];
export type FinderCandidate = Schemas["FinderCandidateResponse"];
export type EventSummary = Schemas["EventSummaryResponse"];
export type EventDetail = Schemas["EventDetailResponse"];
export type RsvpStatus = Schemas["RsvpStatus"];
export type EventStatus = Schemas["EventStatus"];
export type InviteCreated = Schemas["InviteCreatedResponse"];
export type Invite = Schemas["InviteResponse"];
export type Me = Schemas["MeResponse"];
