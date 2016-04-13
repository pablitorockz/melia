----- Melia Script ----------------------------------------------------------
-- Stylist
----- Description -----------------------------------------------------------
-- Allows changing the player's hair style.
----- Notes -----------------------------------------------------------------
-- There are no separate style and color settings, every hair style exists
-- X times, with different colors. Unfortunately there is no strict X styles
-- Y colors convention, some styles have 1 color, some 4, some 5, and
-- the number of available styles differs between genders as well. Splitting
-- into style and color would be bothersome and could break any time.
-----------------------------------------------------------------------------

addnpc(57223, "Stylist", "c_Klaipe", -66, 79, -547, 135, "stylist")

function stylist()
	local pc = getpc()
	local min = 1
	local max = pc.gender == GENDER_MALE and 71 or 82
	local style = pc.hair

	local function setStyle(val)
		style = val
		loopStyle()
	end

	local function modStyle(mod)
		style = style + mod
		loopStyle()
	end

	local function loopStyle()
		if style > max then
			style = min
		elseif style < min then
			style = max
		end
	end

	local selection = select("What can I do for you today?", "Change Hair", "Nothing")
	if selection == 2 then
		msg("Please come back any time.")
		return
	end

	msg("Good decision, one should treat himself once in a while.")

	while true do
		changehair(style)
		selection = select("This is style " .. style .. ", what do you think?", "Next", "Prev", "Jump", "I like it")

		if selection == 1 then
			modStyle(1)
		elseif selection == 2 then
			modStyle(-1)
		elseif selection == 3 then
			setStyle(numinput("Which style would you like to see?", min, max))
		elseif selection == 4 then
			msg("Yes, this style does suit you well. Please come back any time.")
			return
		end
	end
end
