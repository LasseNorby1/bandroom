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

export type IdeaSummary = Schemas["IdeaSummaryResponse"];
export type IdeaDetail = Schemas["IdeaDetailResponse"];
export type IdeaStatus = Schemas["IdeaStatus"];
export type DemoVersion = Schemas["VersionResponse"];
export type StemInfo = Schemas["StemResponse"];
export type StemLabel = Schemas["StemLabel"];
export type PolishJob = Schemas["PolishJobResponse"];
export type CommentInfo = Schemas["CommentResponse"];
export type CommentTarget = Schemas["CommentTarget"];
export type ChannelInfo = Schemas["ChannelResponse"];
export type MessageInfo = Schemas["MessageResponse"];
export type MessagesPage = Schemas["MessagesPage"];
export type Song = Schemas["SongResponse"];
export type SongStatus = Schemas["SongStatus"];
